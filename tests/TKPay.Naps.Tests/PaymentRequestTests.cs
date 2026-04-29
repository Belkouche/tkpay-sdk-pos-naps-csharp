using TKPay.Naps.Models;
using Xunit;

namespace TKPay.Naps.Tests;

public class PaymentRequestTests
{
    [Fact]
    public void Constructor_ValidInput_Succeeds()
    {
        var req = new PaymentRequest(100.00m, "01", "00001");
        Assert.Equal(100.00m, req.Amount);
        Assert.Equal("01", req.RegisterId);
        Assert.Equal("00001", req.CashierId);
        Assert.Null(req.Sequence);
    }

    [Fact]
    public void GetNcai_ConcatenatesRegisterAndCashier()
    {
        var req = new PaymentRequest(50.00m, "02", "00003");
        Assert.Equal("0200003", req.GetNcai());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveAmount_Throws(decimal amount)
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentRequest(amount, "01", "00001"));
    }

    [Fact]
    public void Constructor_RegisterIdWrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentRequest(100m, "1", "00001"));
    }

    [Fact]
    public void Constructor_CashierIdWrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentRequest(100m, "01", "0001"));
    }

    [Fact]
    public void Constructor_SequenceWrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentRequest(100m, "01", "00001", "12345")); // 5 digits, not 6
    }
}
