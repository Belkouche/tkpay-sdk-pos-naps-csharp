using TKPay.Naps;
using TKPay.Naps.Models;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

public class PaymentResultTests
{
    [Fact]
    public void IsApproved_TrueWhenSuccessAndCode000()
    {
        var fields = new Dictionary<string, string>
        {
            [TlvTags.CR]   = "000",
            [TlvTags.Stan] = "000042",
            [TlvTags.Ncar] = "516794******3315",
        };
        var result = PaymentResult.Approved(fields, null, null);
        Assert.True(result.IsApproved());
        Assert.Equal("000042", result.Stan);
        Assert.Equal("516794******3315", result.MaskedCardNumber);
    }

    [Fact]
    public void IsApproved_FalseForFailedResult()
    {
        var result = PaymentResult.Failed("005", new Dictionary<string, string>());
        Assert.False(result.IsApproved());
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("3010", "10/30")]
    [InlineData("2501", "01/25")]
    public void GetFormattedExpiry_ConvertsYymmToMmYy(string input, string expected)
    {
        var fields = new Dictionary<string, string>
        {
            [TlvTags.CR]   = "000",
            [TlvTags.Daex] = input,
        };
        var result = PaymentResult.Approved(fields, null, null);
        Assert.Equal(expected, result.GetFormattedExpiry());
    }

    [Fact]
    public void GetFormattedCardNumber_ReturnsNaWhenNull()
    {
        var result = PaymentResult.Failed("999", new Dictionary<string, string>());
        Assert.Equal("N/A", result.GetFormattedCardNumber());
    }

    [Theory]
    [InlineData("909", "Terminal or server is down")]
    [InlineData("302", "Transaction not found")]
    [InlineData("482", "Transaction already cancelled")]
    public void Failed_SetsHumanReadableError(string code, string expectedSubstring)
    {
        var result = PaymentResult.Failed(code, new Dictionary<string, string>());
        Assert.Contains(expectedSubstring, result.Error);
    }
}
