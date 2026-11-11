using TKPay.Naps.Connection;
using TKPay.Naps.Models;
using TKPay.Naps.Protocol;

namespace TKPay.Naps;

/// <summary>
/// Client for NAPS Pay M2M TLV/TCP terminal integration.
///
/// <para>
/// Performs the complete two-phase payment flow automatically:
/// Phase 1 (TM 001) → customer taps card → Phase 2 (TM 002 on same connection).
/// Phase 2 must be sent within 40 seconds of the Phase-1 response.
/// </para>
///
/// <example>
/// <code>
/// var config = new NapsConfig(host: "192.168.1.100");
/// var client = new NapsPayClient(config);
///
/// var result = await client.ProcessPaymentAsync(
///     new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));
///
/// if (result.IsApproved())
/// {
///     Console.WriteLine($"STAN: {result.Stan}");
///     Console.WriteLine($"Card: {result.GetFormattedCardNumber()}");
///     Console.WriteLine(result.MerchantReceipt?.ToPlainText());
/// }
/// </code>
/// </example>
/// </summary>
public sealed class NapsPayClient : IAsyncDisposable
{
    private readonly NapsConfig _config;
    private int _sequence = 1;
    private readonly object _seqLock = new();
    private SerialConnection? _serial;

    public NapsPayClient(NapsConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Process a card payment.
    ///
    /// Executes Phase 1 (TM 001) and Phase 2 (TM 002) on the same TCP connection.
    /// A TCP connection is opened and closed automatically; a USB serial port stays open between operations.
    /// </summary>
    /// <param name="request">Payment details.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// <see cref="PaymentResult"/> — check <see cref="PaymentResult.IsApproved()"/>.
    /// Throws <see cref="NapsException"/> on connectivity or protocol errors.
    /// </returns>
    public async Task<PaymentResult> ProcessPaymentAsync(
        PaymentRequest request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        await using var conn = await OpenAsync(ct);

        // --- Phase 1: Payment Request ---
        var phase1Fields = await SendPaymentRequestAsync(conn, request, ct);

        var responseCode = phase1Fields.GetValueOrDefault(TlvTags.CR)
            ?? throw NapsException.InvalidResponse("Missing response code (CR/013).");

        if (responseCode != TlvTags.RcApproved)
            return PaymentResult.Failed(responseCode, phase1Fields);

        // --- Phase 2: Confirmation (same connection, ≤ 40 s) ---
        var phase2Fields = await SendConfirmationAsync(conn, request, phase1Fields, ct);

        // The terminal can refuse the confirmation (TM 102): 302 = sale not found,
        // 482 = already cancelled (the terminal auto-reverses). Not an approval.
        var confirmationCode = phase2Fields.GetValueOrDefault(TlvTags.CR);
        if (confirmationCode != null && confirmationCode != TlvTags.RcApproved)
            return PaymentResult.Failed(confirmationCode, phase2Fields);

        return BuildApprovedResult(phase1Fields, phase2Fields);
    }

    /// <summary>
    /// Force end-of-day settlement (telecollecte) — TM=010.
    ///
    /// <para>
    /// Sends batch totals to the NAPS server. The terminal must be idle
    /// ("Attente Caisse") and referencing (TM=013) must have run at least once.
    /// </para>
    ///
    /// <para>
    /// Common failure codes: "052" = already settled today, "909" = terminal/server down.
    /// </para>
    /// </summary>
    /// <param name="registerId">Register ID (2 digits, e.g. "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, e.g. "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<SettlementResult> SettlementAsync(
        string registerId,
        string cashierId,
        CancellationToken ct = default)
    {
        var ncai = registerId + cashierId;
        var tlv = TlvProtocol.BuildSettlementRequest(ncai);

        await using var conn = await OpenAsync(ct);

        var response = await conn.SendAndReceiveAsync(tlv, _config.Timeout, ct);
        var fields = TlvProtocol.Parse(response);

        var responseCode = fields.GetValueOrDefault(TlvTags.CR, "");

        return responseCode == TlvTags.RcApproved
            ? SettlementResult.Accepted(fields)
            : SettlementResult.Declined(responseCode, ReceiptParser.FromFields(fields, ReceiptType.Merchant));
    }

    /// <summary>
    /// Cancel (void) a previous transaction — two-phase, on the same connection
    /// (spec §4.3/§4.4): TM=003 → TM 103 (transaction found, merchant receipt), then
    /// TM=004 → TM 104 (reversal executed, void STAN, customer receipt).
    ///
    /// <para>
    /// Check <see cref="CancellationResult.Status"/>: <c>Completed</c> only on TM 104
    /// CR 000/480. <c>Pending</c> (no TM 104 reply, or CR 995) means the outcome is
    /// unknown — verify with <see cref="DuplicateReceiptAsync"/> (TM 008) by STAN;
    /// never treat it as a completed void.
    /// </para>
    ///
    /// <para>
    /// NapsPay v5.4.4+ requires the original amount (TAG 002) in the TM 003 frame; TM 004
    /// echoes the amount returned in TM 103. A terminal that answers TM 003 directly with
    /// TM 104 is classified without the confirmation step.
    /// </para>
    /// </summary>
    /// <param name="stan">STAN of the transaction to cancel.</param>
    /// <param name="registerId">Register ID (2 digits).</param>
    /// <param name="cashierId">Cashier ID (5 digits).</param>
    /// <param name="sequence">Sequence number of the payment being voided, sent as NSA (005) in
    /// TM 003. The void itself gets a new NS, repeated in TM 004 (spec v2 §4.3/§4.4).</param>
    /// <param name="amountCentimes">Original amount in centimes (required for v5.4.4+).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<CancellationResult> CancelAsync(
        string stan,
        string registerId,
        string cashierId,
        string sequence,
        long? amountCentimes = null,
        CancellationToken ct = default)
    {
        var ncai = registerId + cashierId;
        var voidSequence = GenerateSequence();
        var tlv = TlvProtocol.BuildCancellationRequest(stan, ncai, voidSequence, amountCentimes, sequence);

        await using var conn = await OpenAsync(ct);

        var response = await conn.SendAndReceiveAsync(tlv, _config.Timeout, ct);
        var fields = TlvProtocol.Parse(response);

        var rc = fields.GetValueOrDefault(TlvTags.CR, "");
        var mt = fields.GetValueOrDefault(TlvTags.Tm, "");

        if (mt == TlvTags.MsgCancellationConfirmationResponse)
            return ClassifyCancellationConfirmation(fields, stan, null);

        // TM 103 CR=000 only means "transaction found" — nothing is reversed yet.
        if (mt != TlvTags.MsgCancellationResponse || rc != TlvTags.RcApproved)
            return CancellationResult.Failure(rc);

        Receipt? merchantReceipt = null;
        if (fields.TryGetValue(TlvTags.Dp, out var merchantDp))
            merchantReceipt = ReceiptParser.Parse(merchantDp, ReceiptType.Merchant);
        var originalStan = fields.GetValueOrDefault(TlvTags.Stan) ?? stan;

        // --- Phase 2: Cancellation Confirmation (TM 004 → 104) executes the reversal ---
        var confirmTlv = TlvProtocol.BuildCancellationConfirmationRequest(
            originalStan, ncai, voidSequence,
            fields.GetValueOrDefault(TlvTags.Mt) ?? amountCentimes?.ToString(),
            fields.GetValueOrDefault(TlvTags.DE),
            fields.GetValueOrDefault(TlvTags.Datr),
            fields.GetValueOrDefault(TlvTags.Hetr));
        Dictionary<string, string> confirmation;
        try
        {
            var confirmResponse = await conn.SendAndReceiveAsync(confirmTlv, _config.ConfirmationTimeout, ct);
            confirmation = TlvProtocol.Parse(confirmResponse);
        }
        catch (NapsException ex) when (ex.Code == NapsErrorCode.Timeout)
        {
            // TM 004 was sent but no TM 104 came back: the reversal may or may not
            // have executed. Fail closed — pending, never a completed void.
            return CancellationResult.Pending("", originalStan, merchantReceipt);
        }

        return ClassifyCancellationConfirmation(confirmation, originalStan, merchantReceipt);
    }

    /// <summary>
    /// Classify a TM 104 reply: CR 000/480 completed, CR 995 pending (seen with
    /// successful voids on live v5.4.4 terminals), anything else failed.
    /// </summary>
    private static CancellationResult ClassifyCancellationConfirmation(
        IReadOnlyDictionary<string, string> confirmation, string originalStan, Receipt? merchantReceipt)
    {
        var rc = confirmation.GetValueOrDefault(TlvTags.CR, "");
        switch (rc)
        {
            case TlvTags.RcApproved:
            case TlvTags.RcReversalAccepted:
                var voidStan = confirmation.GetValueOrDefault(TlvTags.Stan) ?? originalStan;
                if (!confirmation.TryGetValue(TlvTags.Dp, out var confirmDp))
                    return CancellationResult.Success(rc, voidStan, merchantReceipt, null);
                // Ingenico Lane3600: TM 103 has no receipt and TM 104 carries the only void
                // receipt ("SIGNATURE CLIENT"), so it is the merchant copy.
                if (merchantReceipt is null)
                    return CancellationResult.Success(
                        rc, voidStan, ReceiptParser.Parse(confirmDp, ReceiptType.Merchant), null);
                return CancellationResult.Success(
                    rc, voidStan, merchantReceipt, ReceiptParser.Parse(confirmDp, ReceiptType.Customer));
            case TlvTags.RcServerProcessingError:
                return CancellationResult.Pending(rc, originalStan, merchantReceipt);
            default:
                return CancellationResult.Failure(rc);
        }
    }

    /// <summary>
    /// Test whether a TCP connection can be established to the terminal.
    /// </summary>
    /// <returns><c>true</c> if reachable, <c>false</c> otherwise.</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await OpenAsync(ct);
            return conn.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Network test / ping (TM=009). Verifies the terminal can reach the NAPS host.
    /// </summary>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<NetworkTestResult> NetworkTestAsync(
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default)
    {
        var fields = await SendManagementAsync(
            TlvProtocol.BuildNetworkTestRequest(registerId + cashierId, GenerateSequence()), ct);
        var rc = fields.GetValueOrDefault(TlvTags.CR, "");
        return new NetworkTestResult
        {
            Success = rc == TlvTags.RcApproved,
            ResponseCode = rc,
            Message = rc == TlvTags.RcApproved ? "Network OK" : TlvProtocol.DescribeResponse(rc)
        };
    }

    /// <summary>
    /// Referencing (TM=013) — loads a terminal parameter set. Sends the TIDE (020)
    /// selecting the set to load.
    /// </summary>
    /// <param name="tide">Parameter set: "01" merchant (default), "02" recharge.</param>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<ReferencingResult> ReferencingAsync(
        string tide = TlvTags.TideMerchant,
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default)
    {
        var fields = await SendManagementAsync(
            TlvProtocol.BuildReferencingRequest(tide, registerId + cashierId, GenerateSequence()), ct);
        var rc = fields.GetValueOrDefault(TlvTags.CR, "");
        return new ReferencingResult
        {
            Success = rc == TlvTags.RcApproved,
            ResponseCode = rc,
            ParameterSet = tide,
            ReceiptText = fields.GetValueOrDefault(TlvTags.Dp),
            Receipt = ReceiptParser.FromFields(fields, ReceiptType.Merchant)
        };
    }

    /// <summary>
    /// Totals report (TM=010). Requests the terminal's running batch totals.
    /// </summary>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task<TotalsResult> TotalsAsync(
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default)
    {
        var fields = await SendManagementAsync(
            TlvProtocol.BuildTotalsRequest(registerId + cashierId, GenerateSequence()), ct);
        var rc = fields.GetValueOrDefault(TlvTags.CR, "");
        return new TotalsResult
        {
            Success = rc == TlvTags.RcApproved,
            ResponseCode = rc,
            ReceiptText = fields.GetValueOrDefault(TlvTags.Dp),
            Receipt = ReceiptParser.FromFields(fields, ReceiptType.Merchant)
        };
    }

    /// <summary>
    /// Duplicate receipt / reprint (TM=008). Reprints the last transaction's receipt.
    /// </summary>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<PaymentResult> DuplicateReceiptAsync(
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default) =>
        SendDuplicateReceiptAsync(registerId, cashierId, null, ct);

    /// <summary>
    /// Duplicate receipt by STAN (TM=008 with STAN). Reprints the receipt of a given
    /// transaction of the current business day — e.g. to check a pending void.
    /// </summary>
    /// <param name="stan">STAN of the transaction to reprint.</param>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public Task<PaymentResult> DuplicateReceiptByStanAsync(
        string stan,
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default) =>
        SendDuplicateReceiptAsync(registerId, cashierId, stan ?? throw new ArgumentNullException(nameof(stan)), ct);

    private async Task<PaymentResult> SendDuplicateReceiptAsync(
        string registerId, string cashierId, string? stan, CancellationToken ct)
    {
        var fields = await SendManagementAsync(
            TlvProtocol.BuildDuplicateReceiptRequest(registerId + cashierId, GenerateSequence(), stan), ct);
        var rc = fields.GetValueOrDefault(TlvTags.CR, "");

        if (rc == TlvTags.RcApproved)
        {
            Receipt? merchantReceipt = null;
            if (fields.TryGetValue(TlvTags.Dp, out var dp))
                merchantReceipt = ReceiptParser.Parse(dp, ReceiptType.Merchant);
            return PaymentResult.Approved(fields, merchantReceipt, null);
        }

        return PaymentResult.Failed(rc, fields);
    }

    /// <summary>
    /// Reset the PIN pad (TM=012). Fire-and-forget maintenance command.
    /// </summary>
    /// <param name="registerId">Register ID (2 digits, default "01").</param>
    /// <param name="cashierId">Cashier ID (5 digits, default "00001").</param>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task ResetPinPadAsync(
        string registerId = "01",
        string cashierId = "00001",
        CancellationToken ct = default)
    {
        await SendManagementAsync(
            TlvProtocol.BuildResetPinpadRequest(registerId + cashierId, GenerateSequence()), ct);
    }

    /// <summary>
    /// Auto-discover the terminal serial port (Ingenico Lane/3000, USB serial),
    /// or <c>null</c> if none is found.
    /// </summary>
    public static string? FindPort() => PortDiscovery.FindIngenicoPort();

    /// <summary>List all available serial ports.</summary>
    public static string[] ListPorts() => PortDiscovery.ListPorts();

    /// <summary>Close the USB serial port kept open between operations (no-op for TCP).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_serial is not null) await _serial.DisposeAsync();
        _serial = null;
    }

    // -------------------------------------------------------------------------
    // Connections
    // -------------------------------------------------------------------------

    /// <summary>
    /// Open the link for one operation. TCP opens a new connection each time. USB serial keeps
    /// one port open for the life of the client: the Ingenico Lane3600 answers late for ~30 s
    /// after the port opens, so reopening it for every operation would delay each one.
    /// </summary>
    private async Task<ITerminalConnection> OpenAsync(CancellationToken ct)
    {
        if (_config.Transport != TransportType.UsbSerial)
        {
            var conn = TerminalConnection.Create(_config);
            await conn.ConnectAsync(ct);
            return conn;
        }
        _serial ??= new SerialConnection(_config);
        if (!_serial.IsConnected) await _serial.ConnectAsync(ct);
        return new KeptOpenConnection(_serial);
    }

    /// <summary>Lends the shared serial connection to one operation; disposing it keeps the port open.</summary>
    private sealed class KeptOpenConnection : ITerminalConnection
    {
        private readonly ITerminalConnection _inner;
        internal KeptOpenConnection(ITerminalConnection inner) => _inner = inner;
        public bool IsConnected => _inner.IsConnected;
        public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
        public Task DisconnectAsync() => _inner.DisconnectAsync();
        public Task<string> SendAndReceiveAsync(string tlvMessage, TimeSpan timeout, CancellationToken ct = default) =>
            _inner.SendAndReceiveAsync(tlvMessage, timeout, ct);
        public ValueTask DisposeAsync() => default;
    }

    // -------------------------------------------------------------------------
    // Management-op transport helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Open a connection, send a single management TLV frame and parse the response.
    /// A TCP connection is opened and closed automatically; a USB serial port stays open between operations.
    /// </summary>
    private async Task<Dictionary<string, string>> SendManagementAsync(
        string tlv, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var response = await conn.SendAndReceiveAsync(tlv, _config.Timeout, ct);
        return TlvProtocol.Parse(response);
    }

    // -------------------------------------------------------------------------
    // Phase 1 — Payment Request
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyDictionary<string, string>> SendPaymentRequestAsync(
        ITerminalConnection conn,
        PaymentRequest request,
        CancellationToken ct)
    {
        var sequence = request.Sequence ?? GenerateSequence();
        var tlv = TlvProtocol.BuildPaymentRequest(request.Amount, request.GetNcai(), sequence);
        var response = await conn.SendAndReceiveAsync(tlv, _config.Timeout, ct);
        return TlvProtocol.Parse(response);
    }

    // -------------------------------------------------------------------------
    // Phase 2 — Confirmation
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyDictionary<string, string>> SendConfirmationAsync(
        ITerminalConnection conn,
        PaymentRequest request,
        IReadOnlyDictionary<string, string> phase1,
        CancellationToken ct)
    {
        var stan = phase1.GetValueOrDefault(TlvTags.Stan)
            ?? throw NapsException.InvalidResponse("Missing STAN (008) in Phase-1 response.");

        var sequence = request.Sequence
            ?? phase1.GetValueOrDefault(TlvTags.Ns)
            ?? throw NapsException.InvalidResponse("Missing sequence (NS/004) in Phase-1 response.");

        // Echo coherence fields from the approved Payment Response (v2 §4.2):
        // amount (MT), masked PAN (NCAR) and expiry (DAEX) when the terminal returned them.
        var amountCentimes = (long)Math.Round(request.Amount * 100m);
        var ncar = phase1.GetValueOrDefault(TlvTags.Ncar);
        var daex = phase1.GetValueOrDefault(TlvTags.Daex);

        var tlv = TlvProtocol.BuildConfirmationRequest(
            stan, request.GetNcai(), sequence, amountCentimes, ncar, daex);
        var response = await conn.SendAndReceiveAsync(tlv, _config.ConfirmationTimeout, ct);
        return TlvProtocol.Parse(response);
    }

    // -------------------------------------------------------------------------
    // Result builder
    // -------------------------------------------------------------------------

    private static PaymentResult BuildApprovedResult(
        IReadOnlyDictionary<string, string> phase1Fields,
        IReadOnlyDictionary<string, string> phase2Fields)
    {
        Receipt? merchantReceipt = null;
        Receipt? customerReceipt = null;

        if (phase1Fields.TryGetValue(TlvTags.Dp, out var merchantDp))
            merchantReceipt = ReceiptParser.Parse(merchantDp, ReceiptType.Merchant);

        if (phase2Fields.TryGetValue(TlvTags.Dp, out var customerDp))
            customerReceipt = ReceiptParser.Parse(customerDp, ReceiptType.Customer);

        return PaymentResult.Approved(phase2Fields, merchantReceipt, customerReceipt);
    }

    // -------------------------------------------------------------------------
    // Sequence generator — thread-safe, wraps at 999999
    // -------------------------------------------------------------------------

    private string GenerateSequence()
    {
        int seq;
        lock (_seqLock)
        {
            seq = _sequence++;
            if (_sequence > 999_999) _sequence = 1;
        }
        return seq.ToString("D6");
    }
}
