using TKPay.Naps;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Tests for the public <see cref="NapsTlv"/> wire-format facade and the static
/// port helpers on <see cref="NapsPayClient"/>.
/// </summary>
public class NapsTlvFacadeTests
{
    [Fact]
    public void BuildField_MatchesTlvFormat()
    {
        Assert.Equal("001003001", NapsTlv.BuildField("001", "001"));
    }

    [Fact]
    public void Parse_ReadsFrame()
    {
        var frame = NapsTlv.BuildField("001", "101") + NapsTlv.BuildField("013", "000");
        var fields = NapsTlv.Parse(frame);

        Assert.Equal("101", fields["001"]);
        Assert.Equal("000", fields["013"]);
    }

    [Theory]
    [InlineData("000", true)]
    [InlineData("007", true)]
    [InlineData("116", false)]
    public void IsApproved_Classifies(string code, bool expected)
    {
        Assert.Equal(expected, NapsTlv.IsApproved(code));
    }

    [Fact]
    public void DescribeResponse_KnownCode()
    {
        Assert.Equal("Approved", NapsTlv.DescribeResponse("000"));
    }

    [Fact]
    public void MaskPan_MasksMiddle()
    {
        Assert.Equal("516794******3315", NapsTlv.MaskPan("5167940123453315"));
    }

    [Fact]
    public void MaskPansInText_MasksInline()
    {
        var masked = NapsTlv.MaskPansInText("PAN 5167940123453315 end");
        Assert.Contains("516794******3315", masked);
        Assert.DoesNotContain("5167940123453315", masked);
    }

    [Fact]
    public void ListPorts_DoesNotThrow()
    {
        // Environment-dependent; just assert it returns a (possibly empty) array.
        var ports = NapsPayClient.ListPorts();
        Assert.NotNull(ports);
    }

    [Fact]
    public void FindPort_DoesNotThrow()
    {
        // May be null when no terminal is attached; must not throw.
        var _ = NapsPayClient.FindPort();
    }
}
