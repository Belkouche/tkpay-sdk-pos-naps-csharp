using System.Text;
using System.Text.RegularExpressions;

namespace TKPay.Naps.Protocol;

/// <summary>
/// Builds and parses NAPS Pay M2M TLV (Tag-Length-Value) messages.
///
/// Format: TAG(3) LENGTH(3) VALUE(n)
/// Example: 001003001 = Tag "001", Length "003", Value "001"
/// </summary>
internal static class TlvProtocol
{
    private static readonly Regex _panPattern = new(@"\b\d{16}\b", RegexOptions.Compiled);

    // -------------------------------------------------------------------------
    // Response codes (§6)
    // -------------------------------------------------------------------------

    /// <summary>CR (013) codes that indicate an approval.</summary>
    internal static readonly HashSet<string> ApprovedCodes =
        new HashSet<string>(StringComparer.Ordinal) { "000", "001", "003", "007" };

    /// <summary>
    /// CR (013) response codes from the v2 spec §6. 000 always means success;
    /// 001/003/007 are also approvals. All others describe a specific condition.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ResponseCodes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["000"] = "Approved",
            ["001"] = "Approved with identification",
            ["003"] = "Approved (VIP)",
            ["007"] = "Approved, microchip update required",
            ["100"] = "Do not honour",
            ["101"] = "Card expired",
            ["102"] = "Fraud suspected",
            ["106"] = "Allowed PIN tries exceeded",
            ["116"] = "Insufficient funds",
            ["117"] = "Incorrect PIN",
            ["118"] = "Card not registered",
            ["120"] = "Transaction not permitted at terminal",
            ["121"] = "Withdrawal limit exceeded",
            ["183"] = "Invalid CVV",
            ["280"] = "Alternative amount cancelled",
            ["302"] = "Record not found on file",
            ["307"] = "Format error",
            ["480"] = "Reversal accepted",
            ["482"] = "Transaction already cancelled",
            ["909"] = "System failure",
            ["911"] = "Issuer did not respond in time",
            ["912"] = "Card issuer unavailable",
            ["995"] = "Server processing error",
        };

    /// <summary>True when the CR code is an approval.</summary>
    internal static bool IsApproved(string? cr) => cr != null && ApprovedCodes.Contains(cr);

    /// <summary>Human-readable description of a CR code, or a fallback string if unknown.</summary>
    internal static string DescribeResponse(string? cr) =>
        cr != null && ResponseCodes.TryGetValue(cr, out var desc)
            ? desc
            : $"Unknown response code: {cr ?? "(none)"}";

    // -------------------------------------------------------------------------
    // Building
    // -------------------------------------------------------------------------

    /// <summary>Encode a single TLV field.</summary>
    internal static string BuildField(string tag, string value)
    {
        var len = value.Length.ToString("D3");
        return $"{tag}{len}{value}";
    }

    /// <summary>
    /// Build Phase-1 payment request TLV.
    /// Sends current date/time in both DA/HE (local clock) and DATR/HETR (transaction).
    /// </summary>
    internal static string BuildPaymentRequest(decimal amount, string ncai, string sequence)
    {
        var amountCentimes = ((long)(amount * 100)).ToString();
        var (date, time) = CurrentDateTime();

        return BuildField(TlvTags.Tm,   TlvTags.MsgPaymentRequest)
             + BuildField(TlvTags.Mt,   amountCentimes)
             + BuildField(TlvTags.Ncai, ncai)
             + BuildField(TlvTags.Ns,   sequence)
             + BuildField(TlvTags.DE,   TlvTags.CurrencyMad)
             + BuildField(TlvTags.Da,   date)
             + BuildField(TlvTags.He,   time)
             + BuildField(TlvTags.Datr, date)
             + BuildField(TlvTags.Hetr, time);
    }

    /// <summary>
    /// Build Phase-2 confirmation request TLV (TM=002).
    /// Must be sent on the same TCP connection within 40 seconds of Phase-1 response.
    ///
    /// <para>
    /// Per v2 §4.2 the confirmation echoes coherence fields from the approved
    /// Payment Response (TM 101): the amount (MT + DE), the masked PAN (NCAR) and
    /// the card expiry (DAEX) when available, so the terminal can cross-check the
    /// frame it is confirming. All echo fields are optional — omit them and the old
    /// three-argument behaviour is preserved.
    /// </para>
    ///
    /// <para>
    /// Field order: TM, NCAI, NS, [MT, DE], STAN, [NCAR], [DAEX], DA, HE.
    /// </para>
    /// </summary>
    internal static string BuildConfirmationRequest(
        string stan,
        string ncai,
        string sequence,
        long? amountCentimes = null,
        string? ncar = null,
        string? daex = null,
        string currency = TlvTags.CurrencyMad)
    {
        var (date, time) = CurrentDateTime();

        var msg = BuildField(TlvTags.Tm,   TlvTags.MsgConfirmationRequest)
                + BuildField(TlvTags.Ncai, ncai)
                + BuildField(TlvTags.Ns,   sequence);

        if (amountCentimes.HasValue)
            msg += BuildField(TlvTags.Mt, amountCentimes.Value.ToString())
                 + BuildField(TlvTags.DE, currency);

        msg += BuildField(TlvTags.Stan, stan);

        if (!string.IsNullOrEmpty(ncar))
            msg += BuildField(TlvTags.Ncar, ncar);
        if (!string.IsNullOrEmpty(daex))
            msg += BuildField(TlvTags.Daex, daex);

        msg += BuildField(TlvTags.Da, date)
             + BuildField(TlvTags.He, time);

        return msg;
    }

    /// <summary>
    /// Build cancellation request TLV (TM=003).
    ///
    /// NapsPay v5.4.4+ requires the original amount (TAG 002) in the frame.
    /// Pass <paramref name="amountCentimes"/> when targeting v5.4.4+.
    /// </summary>
    internal static string BuildCancellationRequest(
        string stan, string ncai, string sequence, long? amountCentimes = null)
    {
        var (date, time) = CurrentDateTime();
        var msg = BuildField(TlvTags.Tm, TlvTags.MsgCancellationRequest);
        if (amountCentimes.HasValue)
            msg += BuildField(TlvTags.Mt, amountCentimes.Value.ToString());
        msg += BuildField(TlvTags.Stan, stan)
             + BuildField(TlvTags.Ncai, ncai)
             + BuildField(TlvTags.Nsa,  sequence)
             + BuildField(TlvTags.Da,   date)
             + BuildField(TlvTags.He,   time);
        return msg;
    }

    /// <summary>
    /// Build settlement request TLV (TM=010 — telecollecte / end-of-day batch).
    /// </summary>
    internal static string BuildSettlementRequest(string ncai)
    {
        var (date, time) = CurrentDateTime();

        return BuildField(TlvTags.Tm,   TlvTags.MsgSettlementRequest)
             + BuildField(TlvTags.Ncai, ncai)
             + BuildField(TlvTags.Da,   date)
             + BuildField(TlvTags.He,   time);
    }

    // -------------------------------------------------------------------------
    // Management / maintenance requests (TM 008/009/010/012/013)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build a management-operation TLV frame: TM + NCAI + NS + DA + HE (+ optional
    /// extra fields). Used by the network test, totals, duplicate-receipt, reset and
    /// referencing operations, which carry no amount.
    /// </summary>
    internal static string BuildManagementRequest(
        string tm,
        string ncai = "0100001",
        string sequence = "000001",
        IEnumerable<(string tag, string value)>? extraFields = null)
    {
        var (date, time) = CurrentDateTime();

        var msg = BuildField(TlvTags.Tm,   tm)
                + BuildField(TlvTags.Ncai, ncai)
                + BuildField(TlvTags.Ns,   sequence)
                + BuildField(TlvTags.Da,   date)
                + BuildField(TlvTags.He,   time);

        if (extraFields != null)
            foreach (var (tag, value) in extraFields)
                msg += BuildField(tag, value);

        return msg;
    }

    /// <summary>Build a network-test request (TM=009).</summary>
    internal static string BuildNetworkTestRequest(string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgNetworkTest, ncai, sequence);

    /// <summary>
    /// Build a referencing request (TM=013) — loads a parameter set on the terminal.
    /// Carries the TIDE (020) selecting the set: "01" merchant, "02" recharge.
    /// </summary>
    internal static string BuildReferencingRequest(
        string tide = TlvTags.TideMerchant, string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgReferencing, ncai, sequence,
            new[] { (TlvTags.Tide, tide) });

    /// <summary>Build a totals-report request (TM=010).</summary>
    internal static string BuildTotalsRequest(string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgTotals, ncai, sequence);

    /// <summary>Build a duplicate-receipt (reprint) request (TM=008).</summary>
    internal static string BuildDuplicateReceiptRequest(string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgDuplicateReceipt, ncai, sequence);

    /// <summary>Build a reset-PIN-pad request (TM=012).</summary>
    internal static string BuildResetPinpadRequest(string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgResetPinpad, ncai, sequence);

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parse a TLV string into a tag → value dictionary.
    /// PAN in tag 007 (NCAR) is masked immediately on parse (PCI DSS).
    /// </summary>
    internal static Dictionary<string, string> Parse(string tlv)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // Stop at the first end-of-message terminator ('!' v2, '?' v1) so any
        // trailing receipt content or noise after it is ignored. Both are accepted
        // so the parser is protocol-version agnostic.
        var term = tlv.IndexOfAny(new[] { '!', '?' });
        if (term >= 0) tlv = tlv[..term];

        var index = 0;

        while (index + 6 <= tlv.Length)
        {
            var tag = tlv.Substring(index, 3);
            var lenStr = tlv.Substring(index + 3, 3);

            if (!int.TryParse(lenStr, out var length)) break;
            if (index + 6 + length > tlv.Length) break;

            var value = tlv.Substring(index + 6, length);

            // Immediately mask the PAN — full PAN must never leave this method
            if (tag == TlvTags.Ncar)
                value = MaskCardNumber(value);

            fields[tag] = value;
            index += 6 + length;
        }

        return fields;
    }

    // -------------------------------------------------------------------------
    // PAN masking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Mask a card number: first 6 + last 4 digits, middle replaced with *.
    /// Example: 5167940123453315 → 516794******3315
    /// </summary>
    internal static string MaskCardNumber(string pan)
    {
        if (pan.Length < 10) return pan;
        var masked = new string('*', pan.Length - 10);
        return pan[..6] + masked + pan[^4..];
    }

    /// <summary>Mask any 16-digit card numbers found in free text (e.g. receipt lines).</summary>
    internal static string MaskCardNumbersInText(string text) =>
        _panPattern.Replace(text, m => MaskCardNumber(m.Value));

    // -------------------------------------------------------------------------
    // Date / Time helpers
    // -------------------------------------------------------------------------

    private static (string date, string time) CurrentDateTime()
    {
        var now = DateTime.Now;
        return (
            date: now.ToString("ddMMyyyy"),
            time: now.ToString("HHmmss")
        );
    }
}
