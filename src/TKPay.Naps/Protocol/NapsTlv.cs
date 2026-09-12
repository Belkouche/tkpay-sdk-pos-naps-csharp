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
    /// Parse a TLV string into a tag → value dictionary. Parsing stops at the first
    /// end-of-message terminator ('!' v2, '?' v1); the PAN in tag 007 is masked on parse.
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
    /// Mask a card number: first 6 + last 4 digits kept, middle replaced with '*'.
    /// Example: <c>5167940123453315</c> → <c>516794******3315</c>.
    /// </summary>
    public static string MaskPan(string pan) =>
        TlvProtocol.MaskCardNumber(pan);

    /// <summary>Mask any 16-digit card numbers found in free text (e.g. receipt lines).</summary>
    public static string MaskPansInText(string text) =>
        TlvProtocol.MaskCardNumbersInText(text);
}
