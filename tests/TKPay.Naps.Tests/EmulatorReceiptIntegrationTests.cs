using System.Text.RegularExpressions;
using TKPay.Naps;
using TKPay.Naps.Models;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Receipts against tkpay-naps-emulator in approve mode: a 10.00 MAD payment must yield
/// the golden merchant (TM 101) and customer (TM 102) copies, and its void the golden
/// TM 103 / TM 104 copies. Date, STAN, transaction and authorisation numbers vary.
/// <code>
/// python3 naps_emulator.py --port 4451
/// NAPS_EMULATOR_MODE=approve NAPS_EMULATOR_PORT=4451 dotnet test --filter Category=Integration
/// </code>
/// </summary>
[Trait("Category", "Integration")]
public class EmulatorReceiptIntegrationTests
{
    private static NapsPayClient Client()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("NAPS_EMULATOR_PORT"), out var p)
            ? p : NapsConfig.DefaultPort;
        return new NapsPayClient(new NapsConfig("127.0.0.1", port,
            timeout: TimeSpan.FromSeconds(10), confirmationTimeout: TimeSpan.FromSeconds(5)));
    }

    private const string Rule = "--------------------------------";

    /// <summary>Golden lines as regexes (literal except the varying values).</summary>
    private static string[] Golden(string label, string copy) =>
        new[]
        {
            "TKpay", Rule, @"\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}", "TKPAY DEMO", "CASABLANCA", Rule,
            "VISA", Regex.Escape("516794******3315"), "Terminal: 00000001", @"Transaction: \d{6}",
            @"Autorisation: \d{6}", @"STAN: \d{6}", Rule, "MONTANT: 10.00 MAD", Rule
        }.Concat(label == "DEBIT" ? new[] { "DEBIT" } : new[] { "ANNULATION" }).Append(copy).ToArray();

    private static void AssertGolden(Receipt? receipt, string label, string copy)
    {
        Assert.NotNull(receipt);
        var lines = receipt!.ToPlainText().Split(Environment.NewLine).Select(l => l.Trim()).ToArray();
        var golden = Golden(label, copy);
        Assert.Equal(golden.Length, lines.Length);
        for (var i = 0; i < golden.Length; i++)
            Assert.Matches("^" + golden[i] + "$", lines[i]);
    }

    [EmulatorFact("approve")]
    public async Task Payment_HasGoldenMerchantAndCustomerCopies_AndVoidToo()
    {
        var client = Client();

        var payment = await client.ProcessPaymentAsync(
            new PaymentRequest(amount: 10.00m, registerId: "01", cashierId: "00001"));

        Assert.True(payment.IsApproved());
        AssertGolden(payment.MerchantReceipt, "DEBIT", "Copie Commerçant");
        AssertGolden(payment.CustomerReceipt, "DEBIT", "Copie Client");
        Assert.Contains($"STAN: {payment.Stan}", payment.CustomerReceipt!.ToPlainText());

        var voidResult = await client.CancelAsync(
            payment.Stan!, "01", "00001", payment.Sequence ?? "000001", amountCentimes: 1000);

        Assert.Equal(CancellationStatus.Completed, voidResult.Status);
        // Live terminals: TM 103 has no receipt; the TM 104 receipt is the merchant copy.
        AssertGolden(voidResult.MerchantReceipt, "ANNULATION", "Copie Commerçant");
        Assert.Null(voidResult.CustomerReceipt);
    }

    [EmulatorFact("approve")]
    public async Task ReportOperations_ReturnParsedReceipts()
    {
        var client = Client();

        var totals = await client.TotalsAsync();
        var referencing = await client.ReferencingAsync();
        var duplicate = await client.DuplicateReceiptAsync();

        Assert.NotEmpty(totals.Receipt!.Lines);
        Assert.NotEmpty(referencing.Receipt!.Lines);
        Assert.NotEmpty(duplicate.MerchantReceipt!.Lines);
        Assert.DoesNotContain("Powered", totals.Receipt.ToPlainText() + referencing.Receipt.ToPlainText());
    }
}
