using TKPay.Naps.Models;

namespace TKPay.Naps.Protocol;

/// <summary>
/// Parses the DP (010) tag value from NAPS Pay into a structured <see cref="Receipt"/>.
///
/// DP contains nested TLV sub-tags per receipt line:
///   030 — line number (2 chars)
///   031 — format: S=Simple, G=Gras/Bold
///   032 — alignment: C=Center, D=Droite(Right), G=Gauche(Left)
///   033 — content (variable)
/// </summary>
internal static class ReceiptParser
{
    internal static Receipt Parse(string dpValue, ReceiptType type)
    {
        var lines = new List<ReceiptLine>();
        var index = 0;

        string lineNumber = "";
        string format    = "S";
        string alignment = "G";
        string content   = "";

        while (index + 6 <= dpValue.Length)
        {
            var tag    = dpValue.Substring(index, 3);
            var lenStr = dpValue.Substring(index + 3, 3);

            if (!int.TryParse(lenStr, out var length)) break;
            if (index + 6 + length > dpValue.Length) break;

            var value = dpValue.Substring(index + 6, length);

            switch (tag)
            {
                case TlvTags.ReceiptLineNumber:
                    // Flush previous line before starting a new one
                    if (lineNumber.Length > 0 && content.Length > 0)
                        lines.Add(MakeLine(lineNumber, content, format, alignment));

                    lineNumber = value;
                    format     = "S";
                    alignment  = "G";
                    content    = "";
                    break;

                case TlvTags.ReceiptFormat:
                    format = value;
                    break;

                case TlvTags.ReceiptAlignment:
                    alignment = value;
                    break;

                case TlvTags.ReceiptContent:
                    content = value;
                    break;
            }

            index += 6 + length;
        }

        // Flush the last line
        if (lineNumber.Length > 0 && content.Length > 0)
            lines.Add(MakeLine(lineNumber, content, format, alignment));

        // TKpay branding + PAN masking
        var branded = ApplyBranding(lines);
        var masked  = branded
            .Select(l => l with { Text = TlvProtocol.MaskCardNumbersInText(l.Text) })
            .ToList();

        return new Receipt(masked, type);
    }

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
            Text:       content,
            Bold:       format == "G",
            Alignment:  alignmentCode switch
            {
                "C" => Alignment.Center,
                "D" => Alignment.Right,
                _   => Alignment.Left
            }
        );

    /// <summary>
    /// Replace the first centred "Naps" header line with "TKPAY" + "Powered by NAPS".
    /// </summary>
    private static List<ReceiptLine> ApplyBranding(List<ReceiptLine> lines)
    {
        var result         = new List<ReceiptLine>(lines.Count + 1);
        var brandingApplied = false;

        foreach (var line in lines)
        {
            if (!brandingApplied
                && line.Alignment == Alignment.Center
                && line.Text.Contains("Naps", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(line with { Text = "TKPAY", Bold = true });

                var nextNum = int.TryParse(line.LineNumber, out var n)
                    ? (n + 1).ToString("D2")
                    : "00";
                result.Add(new ReceiptLine(nextNum, "Powered by NAPS", false, Alignment.Center));
                brandingApplied = true;
            }
            else
            {
                result.Add(line);
            }
        }

        return result;
    }
}
