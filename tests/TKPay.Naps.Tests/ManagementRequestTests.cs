using TKPay.Naps;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Request-building tests for the management operations (TM 008/009/010/012/013)
/// added in 1.4.0: correct TM codes, NCAI/NS presence, and TIDE for referencing.
/// </summary>
public class ManagementRequestTests
{
    [Fact]
    public void NetworkTestRequest_UsesTm009()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildNetworkTestRequest("0100001", "000001"));

        Assert.Equal(TlvTags.MsgNetworkTest, fields[TlvTags.Tm]);
        Assert.Equal("009",     fields[TlvTags.Tm]);
        Assert.Equal("0100001", fields[TlvTags.Ncai]);
        Assert.Equal("000001",  fields[TlvTags.Ns]);
    }

    [Fact]
    public void TotalsRequest_UsesTm010()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildTotalsRequest("0100001", "000001"));

        Assert.Equal("010",     fields[TlvTags.Tm]);
        Assert.Equal("0100001", fields[TlvTags.Ncai]);
    }

    [Fact]
    public void DuplicateReceiptRequest_UsesTm008()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildDuplicateReceiptRequest("0100001", "000001"));

        Assert.Equal("008", fields[TlvTags.Tm]);
    }

    [Fact]
    public void ResetPinpadRequest_UsesTm012()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildResetPinpadRequest("0100001", "000001"));

        Assert.Equal("012", fields[TlvTags.Tm]);
    }

    [Fact]
    public void ReferencingRequest_UsesTm013AndSendsTide()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildReferencingRequest(TlvTags.TideMerchant, "0100001", "000001"));

        Assert.Equal("013", fields[TlvTags.Tm]);
        Assert.Equal("020", TlvTags.Tide);              // TIDE tag is 020
        Assert.Equal("01",  fields[TlvTags.Tide]);       // merchant parameter set
    }

    [Fact]
    public void ReferencingRequest_RechargeTide()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildReferencingRequest(TlvTags.TideRecharge));

        Assert.Equal("013", fields[TlvTags.Tm]);
        Assert.Equal("02",  fields[TlvTags.Tide]);
    }

    [Fact]
    public void ReferencingRequest_DefaultsToMerchantTide()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildReferencingRequest());

        Assert.Equal(TlvTags.TideMerchant, fields[TlvTags.Tide]);
    }

    [Fact]
    public void ManagementRequest_CarriesDateAndTime()
    {
        var fields = TlvProtocol.Parse(TlvProtocol.BuildNetworkTestRequest());

        Assert.True(fields.ContainsKey(TlvTags.Da)); // DDMMYYYY
        Assert.True(fields.ContainsKey(TlvTags.He)); // HHMMSS
        Assert.Equal(8, fields[TlvTags.Da].Length);
        Assert.Equal(6, fields[TlvTags.He].Length);
    }
}
