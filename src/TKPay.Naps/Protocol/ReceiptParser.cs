using TKPay.Naps.Models;

namespace TKPay.Naps.Protocol;

/// <summary>
/// Parses the DP (010) tag value from NAPS Pay into a structured <see cref="Receipt"/>
/// (spec §5).
///
/// DP contains nested TLV sub-tags per receipt line:
///   030 — line number (2 chars)
///   031 — format: S=Simple, G=Gras/Bold
///   032 — alignment: C=Center, D=Droite(Right), G=Gauche(Left)
///   033 — content (variable)
/// Lines are separated by '*'; '?' ends the block.
///
/// <para>
/// The block is read by TLV lengths, so '*' or '?' inside a line's text (e.g. the
/// masked PAN "516794******3315") is kept as text; they are only separators at a
/// field boundary. Every line is kept, in order, exactly as sent — nothing is added,
/// merged or blanked. A card number in the text is masked (first 6 + last 4 digits);
/// the rest of the line is unchanged.
/// </para>
/// </summary>
internal static class ReceiptParser
{
    internal static Receipt Parse(string dpValue, ReceiptType type)
    {
        var lines = new List<ReceiptLine>();

        string? lineNumber = null;
        string? format     = null;
        string? alignment  = null;
        string? content    = null;

        void EndLine()
        {
            if (lineNumber is null && format is null && alignment is null && content is null) return;
            lines.Add(MakeLine(lineNumber ?? "", content ?? "", format ?? "S", alignment ?? "G"));
            lineNumber = format = alignment = content = null;
        }

        var index = 0;
        while (index < dpValue.Length)
        {
            var c = dpValue[index];
            if (c == '*') { EndLine(); index++; continue; }
            if (c == '?') break;
            if (index + 6 > dpValue.Length) break;

            var tag = dpValue.Substring(index, 3);
            if (!int.TryParse(dpValue.Substring(index + 3, 3), out var length)) break;
            if (index + 6 + length > dpValue.Length) break;

            var value = dpValue.Substring(index + 6, length);

            switch (tag)
            {
                case TlvTags.ReceiptLineNumber:
                    if (lineNumber is not null) EndLine(); // next line without a '*' separator
                    lineNumber = value;
                    break;
                case TlvTags.ReceiptFormat:    format    = value; break;
                case TlvTags.ReceiptAlignment: alignment = value; break;
                case TlvTags.ReceiptContent:   content   = value; break;
            }

            index += 6 + length;
        }
        EndLine();

        return new Receipt(lines, type);
    }

    /// <summary>Parse the DP (010) field of a reply, or <c>null</c> when it has none.</summary>
    internal static Receipt? FromFields(IReadOnlyDictionary<string, string> fields, ReceiptType type) =>
        fields.TryGetValue(TlvTags.Dp, out var dp) ? Parse(dp, type) : null;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReceiptLine MakeLine(
        string lineNumber,
        string content,
        string format,
        string alignmentCode) =>
        new(
            LineNumber: lineNumber,
            Text:       TlvProtocol.MaskCardNumbersInText(content),
            Bold:       format == "G",
            Alignment:  alignmentCode switch
            {
                "C" => Alignment.Center,
                "D" => Alignment.Right,
                _   => Alignment.Left
            }
        );
}
