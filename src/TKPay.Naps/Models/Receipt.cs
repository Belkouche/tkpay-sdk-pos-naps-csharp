using System.Text;

namespace TKPay.Naps.Models;

/// <summary>Text alignment for a receipt line.</summary>
public enum Alignment { Left, Center, Right }

/// <summary>Whether this is the merchant or customer copy.</summary>
public enum ReceiptType { Merchant, Customer }

/// <summary>One line on a thermal receipt.</summary>
public sealed record ReceiptLine(
    string LineNumber,
    string Text,
    bool Bold = false,
    Alignment Alignment = Alignment.Left);

/// <summary>Parsed thermal receipt ready to display or print.</summary>
public sealed class Receipt
{
    /// <summary>Ordered list of receipt lines.</summary>
    public IReadOnlyList<ReceiptLine> Lines { get; }

    /// <summary>Merchant or customer copy.</summary>
    public ReceiptType Type { get; }

    internal Receipt(IReadOnlyList<ReceiptLine> lines, ReceiptType type)
    {
        Lines = lines;
        Type = type;
    }

    /// <summary>Plain text representation (lines joined with newline).</summary>
    public string ToPlainText() =>
        string.Join(Environment.NewLine, Lines.Select(l => l.Text));

    /// <summary>
    /// Text with simple formatting prefixes:
    /// [B] = bold, [C]/[R]/[L] = alignment.
    /// </summary>
    public string ToFormattedText()
    {
        var sb = new StringBuilder();
        foreach (var line in Lines)
        {
            if (line.Bold) sb.Append("[B]");
            sb.Append(line.Alignment switch
            {
                Alignment.Center => "[C]",
                Alignment.Right  => "[R]",
                _                => "[L]"
            });
            sb.AppendLine(line.Text);
        }
        return sb.ToString();
    }
}
