using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

public class TlvProtocolTests
{
    // -------------------------------------------------------------------------
    // BuildField
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildField_ProducesCorrectTlv()
    {
        var result = TlvProtocol.BuildField("001", "001");
        Assert.Equal("001003001", result);
    }

    [Fact]
    public void BuildField_PadsLengthToThreeDigits()
    {
        var result = TlvProtocol.BuildField("002", "5000");
        Assert.Equal("0020045000", result);
    }

    // -------------------------------------------------------------------------
    // Parse
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_ReturnsCorrectFields()
    {
        // 001003001  = TM = "001"
        // 013003000  = CR = "000"
        var tlv = "001003001013003000";
        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal("001", fields["001"]);
        Assert.Equal("000", fields["013"]);
    }

    [Fact]
    public void Parse_HandlesMultipleFields()
    {
        var tlv = TlvProtocol.BuildField("001", "101")
                + TlvProtocol.BuildField("008", "000001")
                + TlvProtocol.BuildField("013", "000");

        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal("101",    fields["001"]);
        Assert.Equal("000001", fields["008"]);
        Assert.Equal("000",    fields["013"]);
    }

    [Fact]
    public void Parse_MasksPanImmediately()
    {
        var tlv = TlvProtocol.BuildField("007", "5167940123453315");
        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal("516794******3315", fields["007"]);
        Assert.DoesNotContain("0123453315", fields["007"]); // middle never visible
    }

    [Fact]
    public void Parse_EmptyStringReturnsEmptyDictionary()
    {
        var fields = TlvProtocol.Parse("");
        Assert.Empty(fields);
    }

    [Fact]
    public void Parse_TruncatedTlvStopsGracefully()
    {
        // Valid field followed by truncated one
        var tlv = TlvProtocol.BuildField("001", "001") + "002";
        var fields = TlvProtocol.Parse(tlv);

        Assert.Single(fields);
        Assert.Equal("001", fields["001"]);
    }

    // -------------------------------------------------------------------------
    // MaskCardNumber
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("5167940123453315", "516794******3315")]
    [InlineData("4111111111111111", "411111******1111")]
    [InlineData("1234567890123456", "123456******3456")]
    public void MaskCardNumber_MasksCorrectly(string input, string expected)
    {
        Assert.Equal(expected, TlvProtocol.MaskCardNumber(input));
    }

    [Fact]
    public void MaskCardNumber_ShortInputReturnedUnchanged()
    {
        Assert.Equal("12345", TlvProtocol.MaskCardNumber("12345"));
    }

    // -------------------------------------------------------------------------
    // MaskCardNumbersInText
    // -------------------------------------------------------------------------

    [Fact]
    public void MaskCardNumbersInText_MasksAllPans()
    {
        var text   = "Card: 5167940123453315 and 4111111111111111";
        var result = TlvProtocol.MaskCardNumbersInText(text);

        Assert.Contains("516794******3315", result);
        Assert.Contains("411111******1111", result);
        Assert.DoesNotContain("5167940123453315", result);
    }

    [Fact]
    public void MaskCardNumbersInText_NoCardNumberUnchanged()
    {
        var text = "No card here.";
        Assert.Equal(text, TlvProtocol.MaskCardNumbersInText(text));
    }

    // -------------------------------------------------------------------------
    // BuildPaymentRequest round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildPaymentRequest_ContainsRequiredTags()
    {
        var tlv    = TlvProtocol.BuildPaymentRequest(100.00m, "0100001", "000001");
        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal(TlvTags.MsgPaymentRequest, fields[TlvTags.Tm]);
        Assert.Equal("10000",                   fields[TlvTags.Mt]);   // 100.00 MAD → 10000 centimes
        Assert.Equal("0100001",                 fields[TlvTags.Ncai]);
        Assert.Equal("000001",                  fields[TlvTags.Ns]);
        Assert.Equal(TlvTags.CurrencyMad,       fields[TlvTags.DE]);
    }

    // -------------------------------------------------------------------------
    // BuildConfirmationRequest round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildConfirmationRequest_ContainsRequiredTags()
    {
        var tlv    = TlvProtocol.BuildConfirmationRequest("000042", "0100001", "000001");
        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal(TlvTags.MsgConfirmationRequest, fields[TlvTags.Tm]);
        Assert.Equal("000042",                       fields[TlvTags.Stan]);
        Assert.Equal("0100001",                      fields[TlvTags.Ncai]);
        Assert.Equal("000001",                       fields[TlvTags.Ns]);
    }

    [Fact]
    public void BuildConfirmationRequest_OmitsEchoFieldsWhenNotProvided()
    {
        var tlv    = TlvProtocol.BuildConfirmationRequest("000042", "0100001", "000001");
        var fields = TlvProtocol.Parse(tlv);

        Assert.False(fields.ContainsKey(TlvTags.Mt));   // no amount
        Assert.False(fields.ContainsKey(TlvTags.DE));   // no currency
        Assert.False(fields.ContainsKey(TlvTags.Ncar)); // no PAN
        Assert.False(fields.ContainsKey(TlvTags.Daex)); // no expiry
    }

    [Fact]
    public void BuildConfirmationRequest_CarriesAmountNcarDaexWhenProvided()
    {
        // v2 §4.2 — confirmation echoes MT/DE, NCAR and DAEX from the approved response.
        var tlv = TlvProtocol.BuildConfirmationRequest(
            stan: "000042",
            ncai: "0100001",
            sequence: "000001",
            amountCentimes: 15000,
            ncar: "516794******3315",
            daex: "3010");
        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal("15000",             fields[TlvTags.Mt]);
        Assert.Equal(TlvTags.CurrencyMad, fields[TlvTags.DE]);
        Assert.Equal("000042",            fields[TlvTags.Stan]);
        Assert.Equal("516794******3315",  fields[TlvTags.Ncar]);
        Assert.Equal("3010",              fields[TlvTags.Daex]);
    }

    [Fact]
    public void BuildConfirmationRequest_UsesSpecFieldOrder()
    {
        // Expected order: TM, NCAI, NS, MT, DE, STAN, NCAR, DAEX, DA, HE
        var tlv = TlvProtocol.BuildConfirmationRequest(
            "000042", "0100001", "000001", 15000, "516794******3315", "3010");

        var expected = new[]
        {
            TlvTags.Tm, TlvTags.Ncai, TlvTags.Ns, TlvTags.Mt, TlvTags.DE,
            TlvTags.Stan, TlvTags.Ncar, TlvTags.Daex, TlvTags.Da, TlvTags.He
        };

        // Walk the frame the same way the parser does, collecting tags in wire order.
        var actual = new List<string>();
        var i = 0;
        while (i + 6 <= tlv.Length)
        {
            var tag = tlv.Substring(i, 3);
            var len = int.Parse(tlv.Substring(i + 3, 3));
            actual.Add(tag);
            i += 6 + len;
        }

        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Terminator handling ('!' v2, '?' v1)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData('!')]
    [InlineData('?')]
    public void Parse_StopsAtTerminator(char terminator)
    {
        var tlv = TlvProtocol.BuildField("001", "101")
                + TlvProtocol.BuildField("013", "000")
                + terminator
                + "GARBAGE-AFTER-TERMINATOR";

        var fields = TlvProtocol.Parse(tlv);

        Assert.Equal("101", fields["001"]);
        Assert.Equal("000", fields["013"]);
        Assert.Equal(2, fields.Count); // nothing parsed past the terminator
    }

    // -------------------------------------------------------------------------
    // Response-code table (§6)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("000", "Approved")]
    [InlineData("116", "Insufficient funds")]
    [InlineData("101", "Card expired")]
    [InlineData("482", "Transaction already cancelled")]
    [InlineData("995", "Server processing error")]
    public void DescribeResponse_KnownCodes(string code, string expected)
    {
        Assert.Equal(expected, TlvProtocol.DescribeResponse(code));
    }

    [Fact]
    public void DescribeResponse_UnknownCodeFallsBack()
    {
        Assert.Contains("999", TlvProtocol.DescribeResponse("999"));
    }

    [Theory]
    [InlineData("000", true)]
    [InlineData("001", true)]
    [InlineData("003", true)]
    [InlineData("007", true)]
    [InlineData("116", false)]
    [InlineData(null, false)]
    public void IsApproved_ClassifiesCodes(string? code, bool expected)
    {
        Assert.Equal(expected, TlvProtocol.IsApproved(code));
    }
}
