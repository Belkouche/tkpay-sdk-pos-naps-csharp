using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TKPay.Naps;
using TKPay.Naps.Models;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Card numbers are masked (first 6 + last 4 digits) when the reply is parsed: tag 007
/// and any 13-19 digit number (with or without spaces/dashes) in DP receipt text. The
/// full number must not appear in any result, receipt line, raw field, error message,
/// request frame or captured console/trace output; receipt lines are otherwise unchanged.
/// </summary>
[Collection("ConsoleCapture")]
public class PanMaskingTests
{
    private const string Pan16 = "5167940000123315";
    private const string Pan19 = "6011000990139424123";
    private static readonly string[] FullNumbers = { Pan16, Pan19 };

    private static string F(string tag, string value) => TlvProtocol.BuildField(tag, value);

    private static string Line(string n, string text) =>
        F("030", n) + F("031", "S") + F("032", "G") + F("033", text);

    /// <summary>DP block carrying the PAN plain, spaced and dashed, plus a 19-digit number.</summary>
    private static readonly string Dp = string.Join("*",
        Line("01", "TKpay"),
        Line("02", "Carte: 5167 9400 0012 3315"),
        Line("03", Pan16),
        Line("04", "Ref 6011-0009-9013-9424-123 *OK*"),
        Line("05", "MID: 000000000001  Date 07/11/2026"),
        Line("06", "Copie Client"));

    private static readonly string[] ExpectedLines =
    {
        "TKpay",
        "Carte: 5167 94** **** 3315",
        "516794******3315",
        "Ref 6011-00**-****-***4-123 *OK*",
        "MID: 000000000001  Date 07/11/2026",
        "Copie Client",
    };

    /// <summary>Digits only, so "5167 9400 0012 3315" and "5167-..." are caught too.</summary>
    private static void AssertNoFullNumber(string? text)
    {
        if (text is null) return;
        var digitsOnly = Regex.Replace(text, @"[\s.\-]", "");
        foreach (var full in FullNumbers)
            Assert.DoesNotContain(full, digitsOnly);
    }

    [Theory]
    [InlineData("5167940000123", "516794***0123")]                     // 13 digits
    [InlineData("6011000990139424123", "601100*********4123")]         // 19 digits
    [InlineData("5167 9400 0012 3315", "5167 94** **** 3315")]
    [InlineData("5167-9400-0012-3315", "5167-94**-****-3315")]
    [InlineData("5167.9400.0012.3315", "5167.94**.****.3315")]
    [InlineData("5167\t9400\u00A00012 3315", "5167\t94**\u00A0**** 3315")]
    [InlineData("516794******3315", "516794******3315")]               // already masked
    public void MaskCardNumber_KeepsFirst6Last4AndLength(string input, string expected)
    {
        Assert.Equal(expected, TlvProtocol.MaskCardNumber(input));
        Assert.Equal(input.Length, expected.Length);
    }

    [Theory]
    [InlineData("MID: 000000000001")]          // 12 digits — not a card number
    [InlineData("07/11/2026 00:11:32")]
    [InlineData("STAN: 647138")]
    [InlineData("ID 12345678901234567890123")] // 23-digit run — not a card number
    public void MaskCardNumbersInText_LeavesOtherTextAlone(string text)
    {
        Assert.Equal(text, TlvProtocol.MaskCardNumbersInText(text));
    }

    [Fact]
    public void NapsTlvParse_RawFieldsAreMasked_AndDpStillParses()
    {
        var frame = F("001", "101") + F("013", "000") + F("007", Pan16) + F("010", Dp) + "!";

        var fields = NapsTlv.Parse(frame);

        Assert.Equal("516794******3315", fields["007"]);
        foreach (var value in fields.Values) AssertNoFullNumber(value);
        Assert.Equal(Dp.Length, fields["010"].Length);   // masking keeps TLV lengths valid
        Assert.Equal(ExpectedLines, ReceiptParser.Parse(fields["010"], ReceiptType.Customer).Lines.Select(l => l.Text));
    }

    [Fact]
    public void RawDp_UnknownSubTagWithCardNumber_IsMasked_StructureKept()
    {
        var dp = Line("01", "TKpay") + F("099", Pan16) + "*" + Line("02", "Copie Client");

        var masked = NapsTlv.Parse(F("001", "102") + F("010", dp))["010"];

        AssertNoFullNumber(masked);
        Assert.Equal(dp.Length, masked.Length);
        Assert.Equal(new[] { "TKpay", "Copie Client" },
            ReceiptParser.Parse(masked, ReceiptType.Customer).Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Payment_NoFullNumberInResultsReceiptsRequestsOrOutput()
    {
        var (port, requests) = NapsPay544FlowTests.StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "001"
                ? F("001", "101") + F("004", "000001") + F("013", "000") + F("008", "123456")
                  + F("007", Pan16) + F("017", "3010") + F("010", Dp)
                : F("001", "102") + F("013", "000") + F("008", "123456") + F("007", Pan16) + F("010", Dp));

        var (result, output) = await CaptureOutput(() => NapsPay544FlowTests.Client(port).ProcessPaymentAsync(
            new PaymentRequest(amount: 10.00m, registerId: "01", cashierId: "00001")));
        var frames = await requests;

        Assert.True(result.IsApproved());
        Assert.Equal("516794******3315", result.MaskedCardNumber);
        Assert.Equal(ExpectedLines, result.MerchantReceipt!.Lines.Select(l => l.Text));
        Assert.Equal(ExpectedLines, result.CustomerReceipt!.Lines.Select(l => l.Text));

        foreach (var text in new[]
                 {
                     result.MaskedCardNumber, result.GetFormattedCardNumber(), result.Error,
                     result.MerchantReceipt.ToPlainText(), result.MerchantReceipt.ToFormattedText(),
                     result.CustomerReceipt.ToPlainText(), result.CustomerReceipt.ToFormattedText(), output,
                 })
            AssertNoFullNumber(text);

        // The TM 002 confirmation echoes NCAR — the masked value, never the full number.
        Assert.Equal("516794******3315", TlvProtocol.Parse(frames[1])[TlvTags.Ncar]);
        foreach (var frame in frames) AssertNoFullNumber(frame);
    }

    [Fact]
    public async Task VoidTotalsReferencingSettlementDuplicate_NoFullNumber()
    {
        var reply = (string tm) => F("001", tm) + F("013", "000") + F("008", "123456") + F("007", Pan16) + F("010", Dp);

        var (port1, _) = NapsPay544FlowTests.StartTerminal(frame =>
            reply(TlvProtocol.Parse(frame)[TlvTags.Tm] == "003" ? "103" : "104"));
        var voidResult = await NapsPay544FlowTests.Client(port1).CancelAsync("123456", "01", "00001", "000001", 1000);

        var (port2, _) = NapsPay544FlowTests.StartTerminal(_ => reply("110"));
        var totals = await NapsPay544FlowTests.Client(port2).TotalsAsync();
        var (port3, _) = NapsPay544FlowTests.StartTerminal(_ => reply("113"));
        var referencing = await NapsPay544FlowTests.Client(port3).ReferencingAsync();
        var (port4, _) = NapsPay544FlowTests.StartTerminal(_ => reply("110"));
        var settlement = await NapsPay544FlowTests.Client(port4).SettlementAsync("01", "00001");
        var (port5, _) = NapsPay544FlowTests.StartTerminal(_ => reply("108"));
        var duplicate = await NapsPay544FlowTests.Client(port5).DuplicateReceiptAsync();

        Assert.Equal(ExpectedLines, voidResult.MerchantReceipt!.Lines.Select(l => l.Text));
        Assert.Equal(ExpectedLines, voidResult.CustomerReceipt!.Lines.Select(l => l.Text));
        Assert.Equal(ExpectedLines, totals.Receipt!.Lines.Select(l => l.Text));
        foreach (var text in new[]
                 {
                     voidResult.MerchantReceipt.ToPlainText(), voidResult.CustomerReceipt.ToPlainText(),
                     totals.ReceiptText, totals.Receipt.ToPlainText(),
                     referencing.ReceiptText, referencing.Receipt!.ToPlainText(),
                     settlement.Receipt!.ToPlainText(),
                     duplicate.MaskedCardNumber, duplicate.MerchantReceipt!.ToPlainText(),
                 })
            AssertNoFullNumber(text);
    }

    [Fact]
    public async Task Decline_AndProtocolError_NoFullNumber()
    {
        var (port1, _) = NapsPay544FlowTests.StartTerminal(_ =>
            F("001", "101") + F("013", "116") + F("007", Pan16) + F("010", Dp));
        var declined = await NapsPay544FlowTests.Client(port1).ProcessPaymentAsync(
            new PaymentRequest(amount: 10.00m, registerId: "01", cashierId: "00001"));

        Assert.False(declined.IsApproved());
        Assert.Equal(ExpectedLines, declined.MerchantReceipt!.Lines.Select(l => l.Text));
        AssertNoFullNumber(declined.MaskedCardNumber);
        AssertNoFullNumber(declined.Error);
        AssertNoFullNumber(declined.MerchantReceipt.ToPlainText());

        // Reply without a response code → InvalidResponse; its message must not carry the PAN.
        var (port2, _) = NapsPay544FlowTests.StartTerminal(_ => F("001", "101") + F("007", Pan16) + F("010", Dp));
        var ex = await Assert.ThrowsAsync<NapsException>(() => NapsPay544FlowTests.Client(port2).ProcessPaymentAsync(
            new PaymentRequest(amount: 10.00m, registerId: "01", cashierId: "00001")));
        AssertNoFullNumber(ex.ToString());
    }

    /// <summary>Run <paramref name="action"/> capturing everything written to Console and Trace.</summary>
    private static async Task<(T result, string output)> CaptureOutput<T>(Func<Task<T>> action)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var listener = new TextWriterTraceListener(writer);
        Console.SetOut(writer);
        Console.SetError(writer);
        Trace.Listeners.Add(listener);
        try
        {
            var result = await action();
            Trace.Flush();
            return (result, sb.ToString());
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }
}
