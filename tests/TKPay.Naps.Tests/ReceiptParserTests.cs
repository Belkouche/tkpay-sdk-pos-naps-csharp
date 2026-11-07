using TKPay.Naps;
using TKPay.Naps.Models;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// DP (010) receipt parsing (spec §5) against blocks captured from tkpay-naps-emulator:
/// every line kept in order, exactly as sent, PAN line intact, no SDK-added text.
/// </summary>
public class ReceiptParserTests
{
    // Captured from the emulator (10.00 MAD payment, then its void), 07/11/2026.
    private const string Tm101Frame =
        "0010031010030070100001004006000001013003000002004100001200350401400807112026015006001132007016516794******33150170043010040002CC00800664713801079203000200031001G032001C033005TKpay*03000202031001S032001G033032--------------------------------*03000203031001S032001G03301907/11/2026 00:11:32*03000204031001S032001G033010TKPAY DEMO*03000206031001S032001G033010CASABLANCA*03000207031001S032001G033032--------------------------------*03000209031001S032001G033004VISA*03000210031001S032001G033016516794******3315*03000215031001S032001G033018Terminal: 00000001*03000217031001S032001G033019Transaction: 647138*03000218031001S032001G033020Autorisation: 520076*03000219031001S032001G033012STAN: 647138*03000220031001S032001G033032--------------------------------*03000221031001S032001G033018MONTANT: 10.00 MAD*03000222031001S032001G033032--------------------------------*03000223031001S032001G033005DEBIT*03000224031001S032001G033016Copie Commercant!";

    private const string Tm102Dp =
        "03000200031001G032001C033005TKpay*03000202031001S032001G033032--------------------------------*03000203031001S032001G03301907/11/2026 00:11:32*03000204031001S032001G033010TKPAY DEMO*03000206031001S032001G033010CASABLANCA*03000207031001S032001G033032--------------------------------*03000209031001S032001G033004VISA*03000210031001S032001G033016516794******3315*03000215031001S032001G033018Terminal: 00000001*03000217031001S032001G033019Transaction: 647138*03000218031001S032001G033020Autorisation: 823313*03000219031001S032001G033012STAN: 647138*03000220031001S032001G033032--------------------------------*03000221031001S032001G033018MONTANT: 10.00 MAD*03000222031001S032001G033032--------------------------------*03000223031001S032001G033005DEBIT*03000224031001S032001G033012Copie Client";

    private const string Tm103Dp =
        "03000200031001G032001C033005TKpay*03000202031001S032001G033032--------------------------------*03000203031001S032001G03301907/11/2026 00:11:32*03000204031001S032001G033010TKPAY DEMO*03000206031001S032001G033010CASABLANCA*03000207031001S032001G033032--------------------------------*03000209031001S032001G033004VISA*03000210031001S032001G033016516794******3315*03000215031001S032001G033018Terminal: 00000001*03000217031001S032001G033019Transaction: 647138*03000218031001S032001G033020Autorisation: 196869*03000219031001S032001G033012STAN: 647138*03000220031001S032001G033032--------------------------------*03000221031001S032001G033018MONTANT: 10.00 MAD*03000222031001S032001G033032--------------------------------*03000223031001S032001G033010ANNULATION*03000224031001S032001G033016Copie Commercant";

    private const string Tm104Dp =
        "03000200031001G032001C033005TKpay*03000202031001S032001G033032--------------------------------*03000203031001S032001G03301907/11/2026 00:11:33*03000204031001S032001G033010TKPAY DEMO*03000206031001S032001G033010CASABLANCA*03000207031001S032001G033032--------------------------------*03000209031001S032001G033004VISA*03000210031001S032001G033016516794******3315*03000215031001S032001G033018Terminal: 00000001*03000217031001S032001G033019Transaction: 799172*03000218031001S032001G033020Autorisation: 924439*03000219031001S032001G033012STAN: 799172*03000220031001S032001G033032--------------------------------*03000221031001S032001G033018MONTANT: 10.00 MAD*03000222031001S032001G033032--------------------------------*03000223031001S032001G033010ANNULATION*03000224031001S032001G033012Copie Client";

    private const string Rule = "--------------------------------";

    private static string[] Golden(string date, string stan, string auth, params string[] tail) =>
        new[]
        {
            "TKpay", Rule, date, "TKPAY DEMO", "CASABLANCA", Rule, "VISA", "516794******3315",
            "Terminal: 00000001", $"Transaction: {stan}", $"Autorisation: {auth}", $"STAN: {stan}",
            Rule, "MONTANT: 10.00 MAD", Rule
        }.Concat(tail).ToArray();

    private static string[] Rendered(Receipt r) =>
        r.ToPlainText().Split(Environment.NewLine).Select(l => l.Trim()).ToArray();

    [Fact]
    public void PaymentMerchantCopy_FromTm101Frame_MatchesGolden()
    {
        var dp = TlvProtocol.Parse(Tm101Frame)[TlvTags.Dp];
        var receipt = ReceiptParser.Parse(dp, ReceiptType.Merchant);

        Assert.Equal(Golden("07/11/2026 00:11:32", "647138", "520076", "DEBIT", "Copie Commercant"),
                     Rendered(receipt));
        Assert.Equal(ReceiptType.Merchant, receipt.Type);
    }

    [Fact]
    public void PaymentCustomerCopy_FromTm102_MatchesGolden()
    {
        var receipt = ReceiptParser.Parse(Tm102Dp, ReceiptType.Customer);

        Assert.Equal(Golden("07/11/2026 00:11:32", "647138", "823313", "DEBIT", "Copie Client"),
                     Rendered(receipt));
    }

    [Fact]
    public void VoidCopies_FromTm103AndTm104_MatchGolden()
    {
        Assert.Equal(Golden("07/11/2026 00:11:32", "647138", "196869", "ANNULATION", "Copie Commercant"),
                     Rendered(ReceiptParser.Parse(Tm103Dp, ReceiptType.Merchant)));
        Assert.Equal(Golden("07/11/2026 00:11:33", "799172", "924439", "ANNULATION", "Copie Client"),
                     Rendered(ReceiptParser.Parse(Tm104Dp, ReceiptType.Customer)));
    }

    [Fact]
    public void KeepsLineNumbersBoldAndAlignment()
    {
        var lines = ReceiptParser.Parse(Tm102Dp, ReceiptType.Customer).Lines;

        Assert.Equal(17, lines.Count);
        Assert.Equal(new ReceiptLine("00", "TKpay", Bold: true, Alignment.Center), lines[0]);
        Assert.Equal(new ReceiptLine("10", "516794******3315", Bold: false, Alignment.Left), lines[7]);
        Assert.Equal(new ReceiptLine("24", "Copie Client", Bold: false, Alignment.Left), lines[^1]);
        Assert.Equal(
            new[] { "00", "02", "03", "04", "06", "07", "09", "10", "15", "17", "18", "19", "20", "21", "22", "23", "24" },
            lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void AddsNoText()
    {
        var text = ReceiptParser.Parse(Tm102Dp, ReceiptType.Customer).ToPlainText();

        Assert.DoesNotContain("Powered", text);
        Assert.StartsWith("TKpay", text);
        Assert.EndsWith("Copie Client", text);
    }

    [Fact]
    public void RightAlignedLine_IsRight()
    {
        var lines = ReceiptParser.Parse("03000205031001S032001D033005TOTAL", ReceiptType.Merchant).Lines;

        Assert.Equal(new ReceiptLine("05", "TOTAL", Bold: false, Alignment.Right), Assert.Single(lines));
    }

    [Fact]
    public void FullPanInText_IsMasked_LineKept()
    {
        var lines = ReceiptParser.Parse(
            "03000201031001S032001G0330165167940123453315*03000202031001S032001G033004NEXT",
            ReceiptType.Merchant).Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal("516794******3315", lines[0].Text);
        Assert.Equal("NEXT", lines[1].Text);
    }

    [Fact]
    public void AsteriskAndQuestionMarkInsideText_AreKept()
    {
        var lines = ReceiptParser.Parse(
            "03000201031001S032001G033007A*B?C!D*03000202031001S032001G033006MERCI?",
            ReceiptType.Merchant).Lines;

        Assert.Equal(new[] { "A*B?C!D", "MERCI?" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void QuestionMarkAtLineBoundary_EndsBlock()
    {
        var lines = ReceiptParser.Parse(
            "03000201031001S032001G033003ONE*03000202031001S032001G033003TWO?trailing",
            ReceiptType.Merchant).Lines;

        Assert.Equal(new[] { "ONE", "TWO" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void BlankAndUnseparatedLines_AreKept()
    {
        // An empty content line, and a line that follows without a '*' separator.
        var lines = ReceiptParser.Parse(
            "03000201031001S032001G033000*03000202031001S032001G033001X03000203031001S032001G033001Y",
            ReceiptType.Merchant).Lines;

        Assert.Equal(new[] { "01", "02", "03" }, lines.Select(l => l.LineNumber));
        Assert.Equal(new[] { "", "X", "Y" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void TlvParse_KeepsTerminatorCharactersInsideDp()
    {
        var dp = "03000201031001S032001G033006MERCI!";
        var fields = TlvProtocol.Parse(TlvProtocol.BuildField("001", "104") + TlvProtocol.BuildField("010", dp));

        Assert.Equal(dp, fields[TlvTags.Dp]);
    }

    [Fact]
    public void TlvParse_StopsAtTerminatorOnFieldBoundary()
    {
        var fields = TlvProtocol.Parse("001003101013003000!013003999");

        Assert.Equal("000", fields[TlvTags.CR]);
    }
}
