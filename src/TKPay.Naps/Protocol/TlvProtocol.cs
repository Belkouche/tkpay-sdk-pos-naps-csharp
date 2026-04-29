using System.Text;
using System.Text.RegularExpressions;

namespace TKPay.Naps.Protocol;

/// <summary>
/// Builds and parses NAPS Pay M2M TLV (Tag-Length-Value) messages.
///
/// Format: TAG(3) LENGTH(3) VALUE(n)
/// Example: 001003001 = Tag "001", Length "003", Value "001"
/// </summary>
internal static partial class TlvProtocol
{
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
    /// Build Phase-2 confirmation request TLV.
    /// Must be sent on the same TCP connection within 40 seconds of Phase-1 response.
    /// </summary>
    internal static string BuildConfirmationRequest(string stan, string ncai, string sequence)
    {
        var (date, time) = CurrentDateTime();

        return BuildField(TlvTags.Tm,   TlvTags.MsgConfirmationRequest)
             + BuildField(TlvTags.Stan, stan)
             + BuildField(TlvTags.Ncai, ncai)
             + BuildField(TlvTags.Ns,   sequence)
             + BuildField(TlvTags.Da,   date)
             + BuildField(TlvTags.He,   time)
             + BuildField(TlvTags.Datr, date)
             + BuildField(TlvTags.Hetr, time);
    }

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
        PanPattern().Replace(text, m => MaskCardNumber(m.Value));

    [GeneratedRegex(@"\b\d{16}\b")]
    private static partial Regex PanPattern();

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
