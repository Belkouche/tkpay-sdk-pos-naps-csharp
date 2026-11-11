using System.Text;
using System.Text.RegularExpressions;
using TKPay.Naps.Models;

namespace TKPay.Naps.Protocol;

/// <summary>
/// Builds and parses NAPS Pay M2M TLV (Tag-Length-Value) messages.
///
/// Format: TAG(3) LENGTH(3) VALUE(n)
/// Example: 001003001 = Tag "001", Length "003", Value "001"
/// </summary>
internal static class TlvProtocol
{
    // A card-shaped run in free text: 13 or more digits, each next digit adjacent or
    // after exactly one space, dash, dot, tab or non-breaking space. No upper bound —
    // longer runs are masked too (fail closed).
    private static readonly Regex _panPattern = new(@"(?<!\d)\d(?:[ \-.\t\u00A0]?\d){12,}", RegexOptions.Compiled);

    // -------------------------------------------------------------------------
    // Response codes (§6)
    // -------------------------------------------------------------------------

    /// <summary>CR (013) codes that indicate an approval.</summary>
    internal static readonly HashSet<string> ApprovedCodes =
        new HashSet<string>(StringComparer.Ordinal) { "000", "001", "003", "007" };

    /// <summary>
    /// CR (013) response codes from the v2 spec §6 and the NapsPay ISO response-code
    /// table. 000 always means success; 001/003/007 are also approvals. All others
    /// describe a specific condition.
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
            ["104"] = "Restricted card use",
            ["106"] = "Allowed PIN tries exceeded",
            ["107"] = "Refer to card issuer",
            ["108"] = "Refer to issuer special conditions",
            ["109"] = "Invalid merchant",
            ["110"] = "Invalid amount",
            ["111"] = "Invalid card number",
            ["112"] = "PIN information required",
            ["114"] = "No account of requested type",
            ["115"] = "Requested function not supported",
            ["116"] = "Insufficient funds",
            ["117"] = "Incorrect PIN",
            ["118"] = "Card not registered",
            ["119"] = "Transaction not permitted to cardholder",
            ["120"] = "Transaction not permitted at terminal",
            ["121"] = "Withdrawal limit exceeded",
            ["122"] = "Security violation",
            ["123"] = "Withdrawal frequency exceeded",
            ["125"] = "Card not in service",
            ["126"] = "Invalid PIN format",
            ["127"] = "PIN length error",
            ["128"] = "PIN key sync error",
            ["129"] = "Card counterfeiting suspected",
            ["180"] = "Credit account does not exist",
            ["181"] = "Cheque account does not exist",
            ["182"] = "Savings account does not exist",
            ["183"] = "Invalid CVV",
            ["184"] = "Invalid date",
            ["200"] = "Do not honour — capture card",
            ["201"] = "Card expired — capture card",
            ["202"] = "Fraud suspected — capture card",
            ["204"] = "Restricted card — capture card",
            ["205"] = "Security service — capture card",
            ["206"] = "PIN tries exceeded — capture card",
            ["207"] = "Special conditions — capture card",
            ["208"] = "Lost card — capture card",
            ["209"] = "Stolen card — capture card",
            ["210"] = "Counterfeiting suspected — capture card",
            ["265"] = "Please use the chip",
            ["280"] = "Alternative amount cancelled",
            ["281"] = "PIN verification failed",
            ["299"] = "Capture card",
            ["300"] = "Processing completed",
            ["301"] = "Not supported by recipient",
            ["302"] = "Record not found on file",
            ["303"] = "Duplicate record, old record replaced",
            ["304"] = "Field edit error",
            ["305"] = "File locked",
            ["306"] = "Processing failure",
            ["307"] = "Format error",
            ["308"] = "Duplicate processing, new record rejected",
            ["309"] = "File unknown",
            ["381"] = "Record not found. Account unsettled. Withdrawal not processed",
            ["382"] = "Balance record deleted",
            ["383"] = "Balance updated",
            ["385"] = "Balance enquiry",
            ["400"] = "Reversal approved",
            ["480"] = "Reversal accepted",
            ["481"] = "Reversal amount incorrect",
            ["482"] = "Transaction already cancelled",
            ["500"] = "Reconciliation successful",
            ["503"] = "Totals not available",
            ["581"] = "Reconciliation already done",
            ["582"] = "Reconciliation procedure not available",
            ["800"] = "Cut-off in progress",
            ["880"] = "Connection not accepted",
            ["888"] = "End of cut-off",
            ["902"] = "Invalid transaction",
            ["908"] = "Transaction recipient not referenced for routing",
            ["909"] = "System failure",
            ["911"] = "Issuer did not respond in time",
            ["912"] = "Card issuer unavailable",
            ["992"] = "Issuer not found",
            ["993"] = "PIN verification impossible",
            ["994"] = "Transaction processing error",
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

    /// <summary>Largest reply accepted (replies seen are ~2 KB).</summary>
    internal const int MaxResponseBytes = 64 * 1024;

    /// <summary>
    /// Reader check after each chunk: throws past <see cref="MaxResponseBytes"/>, and walks the
    /// fields (<see cref="IsCompleteMessage"/>) only when the data ends with '!' or '?', so the
    /// buffer is not re-decoded after every chunk. A reply ending otherwise closes on silence.
    /// </summary>
    internal static bool IsCompleteResponse(List<byte> rawBytes, bool byteLengths = false)
    {
        if (rawBytes.Count > MaxResponseBytes)
            throw NapsException.InvalidResponse($"Response larger than {MaxResponseBytes} bytes.");
        var last = rawBytes[rawBytes.Count - 1];
        return (last == (byte)'!' || last == (byte)'?')
            && IsCompleteMessage((byteLengths ? Latin1 : Encoding.UTF8).GetString(rawBytes.ToArray()));
    }

    /// <summary>One character per byte, so a walk over the decoded text counts bytes.</summary>
    internal static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    /// <summary>
    /// Rewrite a frame whose lengths count bytes into one whose lengths count characters. The
    /// Ingenico Lane3600 over USB counts UTF-8 bytes (a 3-byte character in a DP line made a
    /// character-based walk read 2 characters into the next line); the rest of the SDK counts
    /// characters, as the Sunmi does over TCP. <paramref name="latin1"/> holds one character per
    /// received byte. Each value, and each DP sub-tag value, is decoded as UTF-8 and its length
    /// rewritten (DP still capped at 999, as the terminal sends it).
    /// </summary>
    internal static string ByteLengthsToCharLengths(string latin1)
    {
        var sb = new StringBuilder(latin1.Length);
        var pos = 0;
        while (pos < latin1.Length && latin1[pos] is not ('!' or '?'))
        {
            var end = FieldEnd(latin1, pos);
            if (end < 0 || end > latin1.Length) break;
            var tag = latin1.Substring(pos, 3);
            var raw = latin1.Substring(pos + 6, end - pos - 6);
            var value = tag == TlvTags.Dp ? DpBytesToChars(raw) : FromBytes(raw);
            sb.Append(tag).Append(LenField(value.Length)).Append(value);
            pos = end;
        }
        return sb.Append(FromBytes(latin1.Substring(pos))).ToString();
    }

    private static string DpBytesToChars(string dp)
    {
        var sb = new StringBuilder(dp.Length);
        var i = 0;
        while (i < dp.Length)
        {
            if (dp[i] is '*' or '?') { sb.Append(dp[i++]); continue; }
            if (i + 6 > dp.Length || !dp.Substring(i, 6).All(IsAsciiDigit)) break;
            var end = i + 6 + int.Parse(dp.Substring(i + 3, 3));
            if (end > dp.Length) break;
            var value = FromBytes(dp.Substring(i + 6, end - i - 6));
            sb.Append(dp, i, 3).Append(LenField(value.Length)).Append(value);
            i = end;
        }
        return sb.Append(FromBytes(dp.Substring(i))).ToString();
    }

    private static string FromBytes(string latin1) => Encoding.UTF8.GetString(Latin1.GetBytes(latin1));

    private static string LenField(int n) => Math.Min(n, 999).ToString("D3");

    /// <summary>
    /// True once <paramref name="buf"/> holds a complete message: walking the fields by their
    /// length lands on a '!'/'?' terminator. A terminator inside a value (receipt lines end with
    /// '?') is data, so a read that happens to stop right after one is not taken as the end.
    /// If the data is not TLV (the Lane3600 writes 999 as the length of a longer receipt), a
    /// trailing terminator is accepted as the end.
    /// </summary>
    internal static bool IsCompleteMessage(string buf)
    {
        var pos = 0;
        while (pos < buf.Length)
        {
            if (buf[pos] == '!' || buf[pos] == '?') return true;
            if (pos + 6 > buf.Length) return false;
            var end = FieldEnd(buf, pos);
            if (end < 0)
                return buf[buf.Length - 1] == '!' || buf[buf.Length - 1] == '?';
            pos = end;
        }
        return false;
    }

    /// <summary>
    /// Build cancellation request TLV (TM=003), spec v2 §4.3.
    ///
    /// <para>
    /// <paramref name="sequence"/> is the void's own NS (004), a new number independent of the
    /// payment; TM 004 must repeat it. <paramref name="originalSequence"/>, the payment's
    /// sequence, goes in NSA (005). NapsPay v5.4.4+ requires the original amount (TAG 002).
    /// Field order matches the frame the Ingenico Lane3600 accepted: TM, NCAI, NS, DA, HE,
    /// DATR, HETR, STAN, [MT], [NSA]. The previous frame (TM, MT, STAN, NCAI, NSA, DA, HE),
    /// with no NS, was refused by the Lane3600 with CR 307 (format error).
    /// </para>
    /// </summary>
    internal static string BuildCancellationRequest(
        string stan, string ncai, string sequence, long? amountCentimes = null, string? originalSequence = null)
    {
        var (date, time) = CurrentDateTime();
        var msg = BuildField(TlvTags.Tm,   TlvTags.MsgCancellationRequest)
                + BuildField(TlvTags.Ncai, ncai)
                + BuildField(TlvTags.Ns,   sequence)
                + BuildField(TlvTags.Da,   date)
                + BuildField(TlvTags.He,   time)
                + BuildField(TlvTags.Datr, date)
                + BuildField(TlvTags.Hetr, time)
                + BuildField(TlvTags.Stan, stan);
        if (amountCentimes.HasValue)
            msg += BuildField(TlvTags.Mt, amountCentimes.Value.ToString());
        if (!string.IsNullOrEmpty(originalSequence))
            msg += BuildField(TlvTags.Nsa, originalSequence!);
        return msg;
    }

    /// <summary>
    /// Build cancellation confirmation request TLV (TM=004) — Phase 2 of a void (spec §4.4).
    /// Sent on the same connection after TM 103 CR=000; it executes the actual reversal.
    ///
    /// <para>
    /// Spec fields and order: TM, NCAI, NS (same as TM 003), STAN, MT, DE, DATR, HETR, DA, HE.
    /// <paramref name="amount"/>, <paramref name="currency"/>, <paramref name="datr"/> and
    /// <paramref name="hetr"/> are echoed from TM 103; the terminal refuses TM 004 without
    /// the amount (CR 909). No NSA: the sequence goes in NS.
    /// </para>
    /// </summary>
    internal static string BuildCancellationConfirmationRequest(
        string stan, string ncai, string sequence, string? amount = null,
        string? currency = null, string? datr = null, string? hetr = null)
    {
        var (date, time) = CurrentDateTime();
        var msg = BuildField(TlvTags.Tm,   TlvTags.MsgCancellationConfirmationRequest)
                + BuildField(TlvTags.Ncai, ncai)
                + BuildField(TlvTags.Ns,   sequence)
                + BuildField(TlvTags.Stan, stan);
        if (!string.IsNullOrEmpty(amount))
        {
            msg += BuildField(TlvTags.Mt, amount!);
            msg += BuildField(TlvTags.DE, string.IsNullOrEmpty(currency) ? "504" : currency!);
        }
        if (!string.IsNullOrEmpty(datr))
            msg += BuildField(TlvTags.Datr, datr!);
        if (!string.IsNullOrEmpty(hetr))
            msg += BuildField(TlvTags.Hetr, hetr!);
        msg += BuildField(TlvTags.Da, date)
             + BuildField(TlvTags.He, time);
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

    /// <summary>
    /// Build a duplicate-receipt (reprint) request (TM=008). With <paramref name="stan"/>
    /// the terminal reprints that transaction of the current business day (spec §4.6);
    /// without it, the last recorded transaction.
    /// </summary>
    internal static string BuildDuplicateReceiptRequest(
        string ncai = "0100001", string sequence = "000001", string? stan = null) =>
        BuildManagementRequest(TlvTags.MsgDuplicateReceipt, ncai, sequence,
            stan is null ? null : new[] { (TlvTags.Stan, stan) });

    /// <summary>Build a reset-PIN-pad request (TM=012).</summary>
    internal static string BuildResetPinpadRequest(string ncai = "0100001", string sequence = "000001") =>
        BuildManagementRequest(TlvTags.MsgResetPinpad, ncai, sequence);

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// End index (exclusive) of the field starting at <paramref name="pos"/>, -1 if its header
    /// is not TLV. The result can be past the end of <paramref name="data"/> when the field is
    /// not fully received yet. The Ingenico Lane3600 caps the DP length at 999 when the receipt
    /// is longer (a duplicate with both copies is ~2,150 chars), so a DP declared 999 long ends
    /// at its own '?' end marker, found by walking its sub-tags and '*' line separators.
    /// </summary>
    // ASCII only: char.IsDigit accepts other scripts' digits, which int.Parse then rejects.
    internal static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    internal static int FieldEnd(string data, int pos)
    {
        if (pos + 6 > data.Length) return data.Length + 1;
        var header = data.Substring(pos, 6);
        if (!header.All(IsAsciiDigit)) return -1;
        var declaredEnd = pos + 6 + int.Parse(header.Substring(3));
        if (header != TlvTags.Dp + "999") return declaredEnd;

        var i = pos + 6;
        while (i < data.Length)
        {
            if (data[i] == '*') { i++; continue; }
            if (data[i] == '?') return Math.Max(i + 1, declaredEnd);
            if (i + 6 > data.Length) return data.Length + 1; // rest of the receipt not received yet
            var sub = data.Substring(i, 6);
            if (!sub.All(IsAsciiDigit)) return declaredEnd;
            i += 6 + int.Parse(sub.Substring(3));
        }
        return data.Length + 1;
    }

    /// <summary>
    /// Parse a TLV string into a tag → value dictionary.
    /// Card numbers are masked here, before anything else sees the reply: the PAN in tag
    /// 007 (NCAR) and any card number in the receipt text of tag 010 (DP).
    /// </summary>
    internal static Dictionary<string, string> Parse(string tlv)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // Stop at an end-of-message terminator ('!' v2, '?' v1) found at a field
        // boundary, so trailing noise after it is ignored. Lengths drive the walk:
        // a '!' or '?' inside a value (e.g. receipt text in DP) is kept as data.
        var index = 0;

        while (index + 6 <= tlv.Length)
        {
            if (tlv[index] is '!' or '?') break;

            var end = FieldEnd(tlv, index);
            if (end < 0 || end > tlv.Length) break;

            var tag = tlv.Substring(index, 3);
            var value = tlv.Substring(index + 6, end - index - 6);

            // Mask card numbers immediately — the full number never leaves this method.
            if (tag == TlvTags.Ncar)
                value = MaskPan(value);
            else if (tag == TlvTags.Dp)
                value = MaskCardNumbersInDp(value);

            fields[tag] = value;
            index = end;
        }

        return fields;
    }

    // -------------------------------------------------------------------------
    // PAN masking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Mask a card number field (tag 007): always first 6 digits + '*' per hidden digit
    /// + last 4 digits, whatever grouping it arrived in.
    /// Example: "5167 9400 0012 3315" → 516794******3315. A value with 10 digits or fewer
    /// (e.g. already masked) is returned unchanged.
    /// </summary>
    internal static string MaskPan(string pan)
    {
        var digits = new string(pan.Where(char.IsDigit).ToArray());
        if (digits.Length <= 10) return pan;
        return digits[..6] + new string('*', digits.Length - 10) + digits[^4..];
    }

    /// <summary>
    /// Mask a card-shaped run in place: the first 6 and last 4 digits are kept, every
    /// other digit becomes '*'. Separators stay in place, so the length is unchanged.
    /// Example: 5167940123453315 → 516794******3315; "5167 9400 0012 3315" → "5167 94** **** 3315".
    /// </summary>
    internal static string MaskCardNumber(string pan)
    {
        var digits = pan.Count(char.IsDigit);
        if (digits <= 10) return pan;

        var chars = pan.ToCharArray();
        var seen = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsDigit(chars[i])) continue;
            seen++;
            if (seen > 6 && seen <= digits - 4) chars[i] = '*';
        }
        return new string(chars);
    }

    /// <summary>
    /// Mask every card-shaped run in free text (e.g. receipt lines): 13 or more digits,
    /// adjacent or separated by exactly one space, dash, dot, tab or non-breaking space.
    /// Slashes, colons and commas are not separators, so dates, times and amounts are
    /// untouched. The rest of the text and its length are unchanged.
    /// </summary>
    internal static string MaskCardNumbersInText(string text) =>
        _panPattern.Replace(text, m => MaskCardNumber(m.Value));

    /// <summary>
    /// Mask card numbers in every sub-tag value of a raw DP block (line text 033, and
    /// any other or unknown sub-tag), leaving the block structure intact: masking keeps
    /// lengths, so TLV lengths stay valid. The headers are not scanned — they are digits
    /// and would merge with an adjacent number. Anything after a malformed point is
    /// masked as plain text.
    /// </summary>
    private static string MaskCardNumbersInDp(string dp)
    {
        var sb = new StringBuilder(dp.Length);
        var index = 0;
        while (index < dp.Length)
        {
            if (dp[index] is '*' or '?') { sb.Append(dp[index++]); continue; }
            if (index + 6 > dp.Length || !dp.Substring(index + 3, 3).All(IsAsciiDigit))
                break;
            var length = int.Parse(dp.Substring(index + 3, 3));
            if (index + 6 + length > dp.Length)
                break;

            sb.Append(dp, index, 6)
              .Append(MaskCardNumbersInText(dp.Substring(index + 6, length)));
            index += 6 + length;
        }
        if (index < dp.Length)
            sb.Append(MaskCardNumbersInText(dp.Substring(index)));
        return sb.ToString();
    }

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
