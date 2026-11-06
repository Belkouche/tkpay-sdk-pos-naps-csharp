using System.Net;
using System.Net.Sockets;
using System.Text;
using TKPay.Naps;
using TKPay.Naps.Models;
using TKPay.Naps.Protocol;
using Xunit;

namespace TKPay.Naps.Tests;

/// <summary>
/// NapsPay v5.4.4 flow tests against an in-process fake terminal: the two-phase
/// void (TM 003 → 103, TM 004 → 104) and refusal of the payment confirmation (TM 102).
/// </summary>
public class NapsPay544FlowTests
{
    private static string F(string tag, string value) => TlvProtocol.BuildField(tag, value);

    private const string Receipt = "03000201031001S0320001C033005HELLO";

    /// <summary>
    /// Accept one connection; for each request frame received, reply with
    /// <paramref name="reply"/>(frame) + '!' (or stay silent when it returns null).
    /// Returns the port and a task yielding every request frame received.
    /// </summary>
    private static (int port, Task<List<string>> requests) StartTerminal(Func<string, string?> reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var task = Task.Run(async () =>
        {
            var frames = new List<string>();
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buf = new byte[4096];
            try
            {
                while (true)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n == 0) break;
                    var frame = Encoding.UTF8.GetString(buf, 0, n);
                    frames.Add(frame);
                    var resp = reply(frame);
                    if (resp == null) continue;
                    var bytes = Encoding.UTF8.GetBytes(resp + "!");
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }
            }
            catch (IOException) { }
            finally { listener.Stop(); }
            return frames;
        });

        return (port, task);
    }

    private static NapsPayClient Client(int port, TimeSpan? confirmationTimeout = null) =>
        new(new NapsConfig("127.0.0.1", port,
            timeout: TimeSpan.FromSeconds(5),
            confirmationTimeout: confirmationTimeout ?? TimeSpan.FromSeconds(5)));

    // -------------------------------------------------------------------------
    // Request building
    // -------------------------------------------------------------------------

    [Fact]
    public void CancellationConfirmationRequest_UsesTm004WithCancelFields()
    {
        var fields = TlvProtocol.Parse(
            TlvProtocol.BuildCancellationConfirmationRequest("123456", "0100001", "000042", 15000));

        Assert.Equal("004", fields[TlvTags.Tm]);
        Assert.Equal("15000", fields[TlvTags.Mt]);
        Assert.Equal("123456", fields[TlvTags.Stan]);
        Assert.Equal("0100001", fields[TlvTags.Ncai]);
        Assert.Equal("000042", fields[TlvTags.Nsa]);
        Assert.True(fields.ContainsKey(TlvTags.Da));
        Assert.True(fields.ContainsKey(TlvTags.He));
    }

    [Fact]
    public void CancellationRequest_StillUsesTm003()
    {
        var fields = TlvProtocol.Parse(
            TlvProtocol.BuildCancellationRequest("123456", "0100001", "000042", 15000));

        Assert.Equal("003", fields[TlvTags.Tm]);
        Assert.Equal("15000", fields[TlvTags.Mt]);
    }

    [Theory]
    [InlineData("302", "Record not found on file")]
    [InlineData("482", "Transaction already cancelled")]
    [InlineData("902", "Invalid transaction")]
    [InlineData("265", "Please use the chip")]
    [InlineData("481", "Reversal amount incorrect")]
    public void ResponseCodes_CoverNapsPayIsoTable(string code, string expected)
    {
        Assert.Equal(expected, TlvProtocol.DescribeResponse(code));
    }

    // -------------------------------------------------------------------------
    // Two-phase void
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsync_Sends004After103_AndReturnsVoidStanAndReceipts()
    {
        var (port, requests) = StartTerminal(frame =>
        {
            var tm = TlvProtocol.Parse(frame)[TlvTags.Tm];
            return tm switch
            {
                "003" => F("001", "103") + F("003", "0100001") + F("013", "000")
                       + F("018", "06112026") + F("019", "101500") + F("010", Receipt),
                "004" => F("001", "104") + F("003", "0100001") + F("013", "000")
                       + F("008", "000777") + F("010", Receipt),
                _ => null
            };
        });

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.True(result.IsSuccess);
        Assert.Equal("000", result.ResponseCode);
        Assert.Equal("000777", result.Stan);           // void STAN from TM 104
        Assert.NotNull(result.MerchantReceipt);
        Assert.NotNull(result.CustomerReceipt);

        Assert.Equal(2, frames.Count);                 // both phases on one connection
        Assert.Equal("003", TlvProtocol.Parse(frames[0])[TlvTags.Tm]);
        var confirm = TlvProtocol.Parse(frames[1]);
        Assert.Equal("004", confirm[TlvTags.Tm]);
        Assert.Equal("123456", confirm[TlvTags.Stan]);
        Assert.Equal("15000", confirm[TlvTags.Mt]);
    }

    [Fact]
    public async Task CancelAsync_Declined103_DoesNotSend004()
    {
        var (port, requests) = StartTerminal(_ =>
            F("001", "103") + F("003", "0100001") + F("013", "302"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.False(result.IsSuccess);
        Assert.Equal("302", result.ResponseCode);
        Assert.Single(frames);
    }

    [Fact]
    public async Task CancelAsync_Refused104_IsFailure()
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000")
                : F("001", "104") + F("013", "302"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.False(result.IsSuccess);
        Assert.Equal("302", result.ResponseCode);
    }

    [Fact]
    public async Task CancelAsync_No104Reply_StillSucceedsOn103()
    {
        // Void is committed at TM 103; a terminal that never answers TM 004 is tolerated.
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000") + F("008", "123456")
                : null);

        var result = await Client(port, TimeSpan.FromMilliseconds(500))
            .CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.True(result.IsSuccess);
        Assert.Equal("123456", result.Stan);
        Assert.Null(result.CustomerReceipt);
    }

    [Fact]
    public async Task CancelAsync_Direct104_IsAcceptedWithoutConfirmation()
    {
        var (port, requests) = StartTerminal(_ =>
            F("001", "104") + F("013", "000") + F("008", "000777"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.True(result.IsSuccess);
        Assert.Single(frames);
    }

    // -------------------------------------------------------------------------
    // Payment confirmation refusal (TM 102)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("482")]
    [InlineData("302")]
    public async Task ProcessPaymentAsync_Refused102_IsNotApproved(string code)
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "001"
                ? F("001", "101") + F("004", "000001") + F("013", "000") + F("008", "123456")
                : F("001", "102") + F("013", code));

        var result = await Client(port).ProcessPaymentAsync(
            new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));

        Assert.False(result.IsApproved());
        Assert.False(result.Success);
        Assert.Equal(code, result.ResponseCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_Approved102_IsApproved()
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "001"
                ? F("001", "101") + F("004", "000001") + F("013", "000") + F("008", "123456")
                : F("001", "102") + F("013", "000") + F("008", "123456"));

        var result = await Client(port).ProcessPaymentAsync(
            new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));

        Assert.True(result.IsApproved());
        Assert.Equal("123456", result.Stan);
    }
}
