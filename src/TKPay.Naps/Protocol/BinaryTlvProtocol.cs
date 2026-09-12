namespace TKPay.Naps.Protocol;

/// <summary>
/// Binary TLV protocol for Sunmi P2/P3 terminals via TCP socket.
/// Wire format: [2-byte length BE][tag(1) + length(2 BE) + value(N)]...
///
/// <para>
/// This is the binary framing used by some Sunmi deployments, distinct from the
/// ASCII TLV wire format handled by <see cref="TlvProtocol"/>. It is exposed
/// publicly so integrators can build and parse the binary frames directly.
/// </para>
/// </summary>
public static class BinaryTlvProtocol
{
    // Tags
    public const byte TagTxnType = 0x01;
    public const byte TagAmount = 0x02;
    public const byte TagOrderId = 0x03;
    public const byte TagResponseCode = 0x10;
    public const byte TagAuthNumber = 0x11;
    public const byte TagCardNumber = 0x12;
    public const byte TagCardScheme = 0x13;
    public const byte TagRrn = 0x14;
    public const byte TagStan = 0x15;
    public const byte TagTerminalId = 0x16;
    public const byte TagTxnDate = 0x17;
    public const byte TagTxnTime = 0x18;
    public const byte TagApprovalCode = 0x19;
    public const byte TagResponseMsg = 0x1A;

    public const string TxnTypeSale = "01";
    public const string TxnTypeReversal = "02";
    public const string TxnTypeConfirmation = "03";
    public const string TxnTypeCancellation = "04";

    /// <summary>Build a TLV request body (txn type + amount + order id), without the length prefix.</summary>
    public static byte[] BuildRequest(string txnType, string amount, string reference)
    {
        var buffer = new List<byte>();
        AppendTlv(buffer, TagTxnType, System.Text.Encoding.ASCII.GetBytes(txnType));
        AppendTlv(buffer, TagAmount, System.Text.Encoding.ASCII.GetBytes(amount));
        AppendTlv(buffer, TagOrderId, System.Text.Encoding.ASCII.GetBytes(reference));
        return buffer.ToArray();
    }

    /// <summary>Prefix a TLV body with its 2-byte big-endian length.</summary>
    public static byte[] WrapWithLength(byte[] tlvData)
    {
        var result = new byte[2 + tlvData.Length];
        result[0] = (byte)(tlvData.Length >> 8);
        result[1] = (byte)(tlvData.Length & 0xFF);
        Array.Copy(tlvData, 0, result, 2, tlvData.Length);
        return result;
    }

    /// <summary>Parse a binary TLV response body (no length prefix) into a tag → value map.</summary>
    public static Dictionary<byte, string> ParseResponse(byte[] data)
    {
        var fields = new Dictionary<byte, string>();
        var offset = 0;

        while (offset + 2 < data.Length)
        {
            var tag = data[offset++];
            if (offset + 1 >= data.Length) break;
            var length = (data[offset] << 8) | data[offset + 1];
            offset += 2;
            if (offset + length > data.Length) break;
            fields[tag] = System.Text.Encoding.ASCII.GetString(data, offset, length);
            offset += length;
        }

        return fields;
    }

    private static void AppendTlv(List<byte> buffer, byte tag, byte[] value)
    {
        buffer.Add(tag);
        buffer.Add((byte)(value.Length >> 8));
        buffer.Add((byte)(value.Length & 0xFF));
        buffer.AddRange(value);
    }
}
