using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// Round-trip tests for the binary TLV (Sunmi) protocol:
/// tag(1) + length(2 BE) + value(N), with a 2-byte BE length prefix.
/// </summary>
public class BinaryTlvProtocolTests
{
    [Fact]
    public void BuildRequest_EncodesTagLengthValueTriples()
    {
        var body = BinaryTlvProtocol.BuildRequest(
            BinaryTlvProtocol.TxnTypeSale, "10000", "ORDER-1");

        // First triple: tag 0x01, len 0x0002 ("01")
        Assert.Equal(BinaryTlvProtocol.TagTxnType, body[0]);
        Assert.Equal(0x00, body[1]);
        Assert.Equal(0x02, body[2]);
        Assert.Equal((byte)'0', body[3]);
        Assert.Equal((byte)'1', body[4]);
    }

    [Fact]
    public void WrapWithLength_PrefixesBigEndianLength()
    {
        var body = new byte[] { 0xAA, 0xBB, 0xCC };
        var wrapped = BinaryTlvProtocol.WrapWithLength(body);

        Assert.Equal(5, wrapped.Length);
        Assert.Equal(0x00, wrapped[0]);      // length high byte
        Assert.Equal(0x03, wrapped[1]);      // length low byte
        Assert.Equal(0xAA, wrapped[2]);
        Assert.Equal(0xCC, wrapped[4]);
    }

    [Fact]
    public void BuildThenParse_RoundTrips()
    {
        var body = BinaryTlvProtocol.BuildRequest(
            BinaryTlvProtocol.TxnTypeSale, "15000", "REF-42");

        var parsed = BinaryTlvProtocol.ParseResponse(body);

        Assert.Equal(BinaryTlvProtocol.TxnTypeSale, parsed[BinaryTlvProtocol.TagTxnType]);
        Assert.Equal("15000", parsed[BinaryTlvProtocol.TagAmount]);
        Assert.Equal("REF-42", parsed[BinaryTlvProtocol.TagOrderId]);
    }

    [Fact]
    public void ParseResponse_HandlesMultipleResponseTags()
    {
        // Hand-build a response: response code "000", auth "123456", STAN "000042".
        var buf = new List<byte>();
        void Append(byte tag, string value)
        {
            var v = System.Text.Encoding.ASCII.GetBytes(value);
            buf.Add(tag);
            buf.Add((byte)(v.Length >> 8));
            buf.Add((byte)(v.Length & 0xFF));
            buf.AddRange(v);
        }
        Append(BinaryTlvProtocol.TagResponseCode, "000");
        Append(BinaryTlvProtocol.TagApprovalCode, "123456");
        Append(BinaryTlvProtocol.TagStan, "000042");

        var parsed = BinaryTlvProtocol.ParseResponse(buf.ToArray());

        Assert.Equal("000",    parsed[BinaryTlvProtocol.TagResponseCode]);
        Assert.Equal("123456", parsed[BinaryTlvProtocol.TagApprovalCode]);
        Assert.Equal("000042", parsed[BinaryTlvProtocol.TagStan]);
    }

    [Fact]
    public void ParseResponse_EmptyReturnsEmpty()
    {
        Assert.Empty(BinaryTlvProtocol.ParseResponse(System.Array.Empty<byte>()));
    }

    [Fact]
    public void ParseResponse_TruncatedStopsGracefully()
    {
        // Declares length 10 but only supplies 2 value bytes.
        var data = new byte[] { BinaryTlvProtocol.TagAmount, 0x00, 0x0A, (byte)'1', (byte)'2' };
        var parsed = BinaryTlvProtocol.ParseResponse(data);

        Assert.Empty(parsed); // over-run detected, nothing emitted
    }
}
