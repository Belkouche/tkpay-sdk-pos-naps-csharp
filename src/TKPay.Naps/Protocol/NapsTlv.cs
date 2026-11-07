namespace TKPay.Naps.Protocol;

/// <summary>
/// Public facade over the NAPS Pay ASCII TLV wire format.
///
/// <para>
/// The core builder/parser (<c>TlvProtocol</c>) is internal by design so its
/// callers stay encapsulated. This facade re-exposes the pure, side-effect-free
/// helpers integrators need to work with the wire format directly — encoding a
/// field, parsing a frame, classifying a response code and masking a PAN — the
/// same surface the standalone aslan SDK offered as a public protocol class.
/// </para>
/// </summary>
public static class NapsTlv
{
    /// <summary>Encode a single TLV field as <c>TAG(3) LENGTH(3) VALUE(n)</c>.</summary>
    public static string BuildField(string tag, string value) =>
        TlvProtocol.BuildField(tag, value);

    /// <summary>
    /// Parse a TLV string into a tag → value dictionary. Parsing stops at an
    /// end-of-message terminator ('!' v2, '?' v1) on a field boundary. Card numbers
    /// (tag 007, and any in the DP receipt text) are masked (first 6 + last 4 digits).
    /// </summary>
    public static Dictionary<string, string> Parse(string tlv) =>
        TlvProtocol.Parse(tlv);

    /// <summary>True when the CR (013) response code is an approval (000/001/003/007).</summary>
    public static bool IsApproved(string? responseCode) =>
        TlvProtocol.IsApproved(responseCode);

    /// <summary>Human-readable description of a CR (013) response code (§6).</summary>
    public static string DescribeResponse(string? responseCode) =>
        TlvProtocol.DescribeResponse(responseCode);

    /// <summary>
    /// Mask a card number: first 6 + last 4 digits kept, one '*' per hidden digit,
    /// grouping removed. Example: <c>5167 9400 0012 3315</c> → <c>516794******3315</c>.
    /// </summary>
    public static string MaskPan(string pan) =>
        TlvProtocol.MaskPan(pan);

    /// <summary>
    /// Mask every card number found in free text (e.g. receipt lines):
    /// 13 or more digits, adjacent or separated by exactly one space, dash, dot, tab or
    /// non-breaking space; first 6 + last 4 digits kept, separators and length unchanged.
    /// </summary>
    public static string MaskPansInText(string text) =>
        TlvProtocol.MaskCardNumbersInText(text);
}
