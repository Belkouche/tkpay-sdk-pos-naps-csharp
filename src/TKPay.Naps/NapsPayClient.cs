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
public sealed class NapsPayClient
{
    private readonly NapsConfig _config;
    private int _sequence = 1;
    private readonly object _seqLock = new();

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
    /// The connection is opened and closed automatically.
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
        ArgumentNullException.ThrowIfNull(request);

        await using var conn = new NapsConnection(_config);
        await conn.ConnectAsync(ct);

        // --- Phase 1: Payment Request ---
        var phase1Fields = await SendPaymentRequestAsync(conn, request, ct);

        var responseCode = phase1Fields.GetValueOrDefault(TlvTags.CR)
            ?? throw NapsException.InvalidResponse("Missing response code (CR/013).");

        if (responseCode != TlvTags.RcApproved)
            return PaymentResult.Failed(responseCode, phase1Fields);

        // --- Phase 2: Confirmation (same connection, ≤ 40 s) ---
        var phase2Fields = await SendConfirmationAsync(conn, request, phase1Fields, ct);

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

        await using var conn = new NapsConnection(_config);
        await conn.ConnectAsync(ct);

        var response = await conn.SendAndReceiveAsync(tlv, _config.Timeout, ct);
        var fields = TlvProtocol.Parse(response);

        var responseCode = fields.GetValueOrDefault(TlvTags.CR, "");

        return responseCode == TlvTags.RcApproved
            ? SettlementResult.Accepted(fields)
            : SettlementResult.Declined(responseCode);
    }

    /// <summary>
    /// Test whether a TCP connection can be established to the terminal.
    /// </summary>
    /// <returns><c>true</c> if reachable, <c>false</c> otherwise.</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NapsConnection(_config);
            await conn.ConnectAsync(ct);
            return conn.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Phase 1 — Payment Request
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyDictionary<string, string>> SendPaymentRequestAsync(
        NapsConnection conn,
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
        NapsConnection conn,
        PaymentRequest request,
        IReadOnlyDictionary<string, string> phase1,
        CancellationToken ct)
    {
        var stan = phase1.GetValueOrDefault(TlvTags.Stan)
            ?? throw NapsException.InvalidResponse("Missing STAN (008) in Phase-1 response.");

        var sequence = request.Sequence
            ?? phase1.GetValueOrDefault(TlvTags.Ns)
            ?? throw NapsException.InvalidResponse("Missing sequence (NS/004) in Phase-1 response.");

        var tlv = TlvProtocol.BuildConfirmationRequest(stan, request.GetNcai(), sequence);
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
