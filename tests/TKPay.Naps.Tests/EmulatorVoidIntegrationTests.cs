using TKPay.Naps;
using TKPay.Naps.Models;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Runs only when <c>NAPS_EMULATOR_MODE</c> equals <paramref name="mode"/>; otherwise skipped.
/// The emulator must be started in that mode beforehand (see <see cref="EmulatorVoidIntegrationTests"/>).
/// </summary>
public sealed class EmulatorFactAttribute : FactAttribute
{
    public EmulatorFactAttribute(string mode)
    {
        if (Environment.GetEnvironmentVariable("NAPS_EMULATOR_MODE") != mode)
            Skip = $"Requires the emulator in --mode {mode} (set NAPS_EMULATOR_MODE={mode}).";
    }
}

/// <summary>
/// Void (TM 003 → 103, TM 004 → 104) against tkpay-naps-emulator, one test per emulator mode:
/// <code>
/// python3 naps_emulator.py --port 4451 --mode void_480
/// NAPS_EMULATOR_MODE=void_480 NAPS_EMULATOR_PORT=4451 dotnet test --filter Category=Integration
/// </code>
/// void_refuse is expected with <c>--code 302</c>.
/// </summary>
[Trait("Category", "Integration")]
public class EmulatorVoidIntegrationTests
{
    private const string Stan = "123456";

    private static Task<CancellationResult> Cancel()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("NAPS_EMULATOR_PORT"), out var p)
            ? p : NapsConfig.DefaultPort;
        var client = new NapsPayClient(new NapsConfig("127.0.0.1", port,
            timeout: TimeSpan.FromSeconds(10), confirmationTimeout: TimeSpan.FromSeconds(3)));
        return client.CancelAsync(Stan, "01", "00001", "000042", 15000);
    }

    [EmulatorFact("approve")]
    public async Task Approve_VoidCompletes()
    {
        var r = await Cancel();

        Assert.Equal(CancellationStatus.Completed, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("000", r.ResponseCode);
        Assert.NotNull(r.Stan);
        // Live terminals: TM 103 has no receipt; the TM 104 receipt is the merchant copy.
        Assert.NotNull(r.MerchantReceipt);
        Assert.Null(r.CustomerReceipt);
    }

    [EmulatorFact("void_480")]
    public async Task Void480_Completes()
    {
        var r = await Cancel();

        Assert.Equal(CancellationStatus.Completed, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("480", r.ResponseCode);
        Assert.NotNull(r.Stan);
    }

    [EmulatorFact("void_995")]
    public async Task Void995_IsPending_KeepsStan()
    {
        var r = await Cancel();

        Assert.Equal(CancellationStatus.Pending, r.Status);
        Assert.False(r.IsSuccess);
        Assert.Equal("995", r.ResponseCode);
        Assert.Equal(Stan, r.Stan);
        Assert.Null(r.MerchantReceipt); // TM 103 carries no receipt on live terminals
    }

    [EmulatorFact("void_no_confirm")]
    public async Task VoidNoConfirm_IsPending_KeepsStan()
    {
        var r = await Cancel();

        Assert.Equal(CancellationStatus.Pending, r.Status);
        Assert.False(r.IsSuccess);
        Assert.Equal("", r.ResponseCode);
        Assert.Equal(Stan, r.Stan);
        Assert.Null(r.MerchantReceipt); // TM 103 carries no receipt on live terminals
    }

    [EmulatorFact("void_refuse")]
    public async Task VoidRefuse_IsFailure_NotPending()
    {
        var r = await Cancel();

        Assert.Equal(CancellationStatus.Failed, r.Status);
        Assert.False(r.IsSuccess);
        Assert.False(r.IsPending);
        Assert.Equal("302", r.ResponseCode);
    }
}
