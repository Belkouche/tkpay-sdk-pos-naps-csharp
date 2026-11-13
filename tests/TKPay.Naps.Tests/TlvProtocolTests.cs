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

    [Theory]
    [InlineData("001003109013003000!", true)]
    [InlineData("001003109013003000", false)]
    [InlineData("001003108010012LINE1?LINE2?", false)]          // '?' inside the DP value
    [InlineData("001003108010012LINE1?LINE2?013003000!", true)]
    [InlineData("001003108010014MERCI! A BIENT", false)]        // '!' inside the DP value
    [InlineData("00100", false)]
    [InlineData("garbage!", true)]                              // non-TLV: trailing terminator
    [InlineData("garbage", false)]
    public void IsCompleteMessage_OnlyAtTerminatorOnFieldBoundary(string buf, bool expected) =>
        Assert.Equal(expected, TlvProtocol.IsCompleteMessage(buf));

    [Fact]
    public void IsCompleteResponse_WalksOnlyWhenDataEndsWithTerminator_AndCapsSize()
    {
        var bytes = (string str) => System.Text.Encoding.UTF8.GetBytes(str).ToList();
        Assert.True(TlvProtocol.IsCompleteResponse(bytes("001003109013003000!")));
        Assert.False(TlvProtocol.IsCompleteResponse(bytes("001003109013003000!0")));   // closes on silence
        Assert.False(TlvProtocol.IsCompleteResponse(bytes("001003108010012LINE1?LINE2?")));
        var huge = Enumerable.Repeat((byte)'0', TlvProtocol.MaxResponseBytes + 1).ToList();
        Assert.Throws<TKPay.Naps.Models.NapsException>(() => TlvProtocol.IsCompleteResponse(huge));
    }

    // A receipt longer than 999 chars, as the Lane3600 sends it: DP declared 999 long.
    private static readonly string LongDp = string.Concat(Enumerable.Range(0, 25).Select(n =>
        $"*030002{n:D2}031001S032001G033040{new string('X', 40)}")) + "?";
    private static readonly string LongFrame = "001003108" + "010999" + LongDp + "013003000" + "!";

    [Fact]
    public void Parse_DpDeclared999_ReadsWholeReceiptAndFollowingFields()
    {
        var fields = TlvProtocol.Parse(LongFrame);
        Assert.Equal(LongDp, fields[TlvTags.Dp]);
        Assert.Equal("000", fields[TlvTags.CR]);
    }

    [Fact]
    public void IsCompleteMessage_DpDeclared999_WaitsForWholeReceipt()
    {
        Assert.False(TlvProtocol.IsCompleteMessage(LongFrame.Substring(0, 1200)));
        Assert.False(TlvProtocol.IsCompleteMessage(LongFrame.Substring(0, LongFrame.Length - 1)));
        Assert.True(TlvProtocol.IsCompleteMessage(LongFrame));
    }

    [Fact]
    public void Parse_NonAsciiDigitsInHeader_StopsWithoutThrowing()
    {
        // Arabic-Indic digits pass char.IsDigit but not int.Parse.
        var fields = TlvProtocol.Parse("001003109" + "013\u0660\u0660\u0663000!");
        Assert.Equal("109", fields[TlvTags.Tm]);
        Assert.False(fields.ContainsKey(TlvTags.CR));
        Assert.False(TlvProtocol.IsCompleteMessage("013\u0660\u0660\u0663000"));
    }

    [Fact]
    public void Parse_NegativeLengthInsideDp_DoesNotThrow()
    {
        var frame = "001003108" + TlvProtocol.BuildField(TlvTags.Dp, "033-01X?") + "013003000!";
        var fields = TlvProtocol.Parse(frame);
        Assert.Equal("000", fields[TlvTags.CR]);
        ReceiptParser.Parse(fields[TlvTags.Dp], TKPay.Naps.Models.ReceiptType.Merchant); // must not throw
    }

    [Fact]
    public void ByteLengthsToCharLengths_RewritesFieldAndDpSubTagLengths()
    {
        // "Copie Commerçé" is 14 chars but 16 UTF-8 bytes; the Lane3600 declares 016.
        var dp = "*03000201033016Copie Commerçé*03000202033004NEXT?";
        var dpBytes = System.Text.Encoding.UTF8.GetByteCount(dp);
        var frame = System.Text.Encoding.UTF8.GetBytes($"001003108010{dpBytes:D3}{dp}013003000!");

        var converted = TlvProtocol.ByteLengthsToCharLengths(TlvProtocol.Latin1.GetString(frame));
        var fields = TlvProtocol.Parse(converted);

        Assert.Equal(dp.Replace("033016", "033014"), fields[TlvTags.Dp]); // length now in characters
        Assert.Equal("000", fields[TlvTags.CR]);
        Assert.Equal(LongFrame, TlvProtocol.ByteLengthsToCharLengths(LongFrame)); // ASCII unchanged
    }

    [Fact]
    public void Parse_DpReally999WithoutEndMarker_DoesNotSwallowNextFields()
    {
        var dp = "";
        var n = 0;
        while (999 - dp.Length > 75) dp += $"*030002{n++ % 100:D2}031001S032001G033040{new string('X', 40)}";
        var rest = 999 - dp.Length - 7;
        dp += $"*033{rest:D3}{new string('X', rest)}"; // exactly 999 chars, no end marker

        var fields = TlvProtocol.Parse("001003108" + "010999" + dp + "013003000" + "0070165167940000003315" + "!");

        Assert.Equal(dp, fields[TlvTags.Dp]);
        Assert.Equal("000", fields[TlvTags.CR]);
        Assert.Equal("516794******3315", fields[TlvTags.Ncar]);
    }
}
