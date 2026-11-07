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
    internal static (int port, Task<List<string>> requests) StartTerminal(Func<string, string?> reply)
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

    internal static NapsPayClient Client(int port, TimeSpan? confirmationTimeout = null) =>
        new(new NapsConfig("127.0.0.1", port,
            timeout: TimeSpan.FromSeconds(5),
            confirmationTimeout: confirmationTimeout ?? TimeSpan.FromSeconds(5)));

    // -------------------------------------------------------------------------
    // Request building
    // -------------------------------------------------------------------------

    [Fact]
    public void CancellationConfirmationRequest_UsesTm004_SequenceInNsNotNsa()
    {
        var fields = TlvProtocol.Parse(
            TlvProtocol.BuildCancellationConfirmationRequest("123456", "0100001", "000042", "000000015000"));

        Assert.Equal("004", fields[TlvTags.Tm]);
        Assert.Equal("000000015000", fields[TlvTags.Mt]);   // echoed verbatim from TM 103
        Assert.Equal("123456", fields[TlvTags.Stan]);
        Assert.Equal("0100001", fields[TlvTags.Ncai]);
        Assert.Equal("000042", fields[TlvTags.Ns]);         // spec §4.4: 004.NS = 003 sequence
        Assert.False(fields.ContainsKey(TlvTags.Nsa));
        Assert.True(fields.ContainsKey(TlvTags.Da));
        Assert.True(fields.ContainsKey(TlvTags.He));
    }

    [Fact]
    public void CancellationConfirmationRequest_LiveVerifiedFieldOrder()
    {
        var tlv = TlvProtocol.BuildCancellationConfirmationRequest("123456", "0100001", "000042", "15000");

        Assert.StartsWith(
            F("001", "004") + F("002", "15000") + F("008", "123456") + F("003", "0100001") + F("004", "000042") + "014008",
            tlv);
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
                "003" => F("001", "103") + F("003", "0100001") + F("013", "000") + F("008", "123456")
                       + F("002", "000000015000") + F("012", "504")
                       + F("018", "06112026") + F("019", "101500") + F("010", Receipt),
                "004" => F("001", "104") + F("003", "0100001") + F("013", "000")
                       + F("008", "000777") + F("010", Receipt),
                _ => null
            };
        });

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.True(result.IsSuccess);
        Assert.Equal(CancellationStatus.Completed, result.Status);
        Assert.Equal("000", result.ResponseCode);
        Assert.Equal("000777", result.Stan);           // void STAN from TM 104
        Assert.NotNull(result.MerchantReceipt);
        Assert.NotNull(result.CustomerReceipt);

        Assert.Equal(2, frames.Count);                 // both phases on one connection
        Assert.Equal("003", TlvProtocol.Parse(frames[0])[TlvTags.Tm]);
        var confirm = TlvProtocol.Parse(frames[1]);
        Assert.Equal("004", confirm[TlvTags.Tm]);
        Assert.Equal("123456", confirm[TlvTags.Stan]);
        Assert.Equal("000000015000", confirm[TlvTags.Mt]);  // MT echoed from TM 103
        Assert.Equal("000042", confirm[TlvTags.Ns]);
        Assert.False(confirm.ContainsKey(TlvTags.Nsa));
    }

    [Fact]
    public async Task CancelAsync_103WithoutMt_FallsBackToRequestAmount()
    {
        var (port, requests) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000") + F("008", "123456")
                : F("001", "104") + F("013", "000") + F("008", "000777"));

        await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.Equal("15000", TlvProtocol.Parse(frames[1])[TlvTags.Mt]);
    }

    [Fact]
    public async Task CancelAsync_104Cr480_IsCompleted()
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000") + F("008", "123456")
                : F("001", "104") + F("013", "480") + F("008", "000777"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.True(result.IsSuccess);
        Assert.Equal(CancellationStatus.Completed, result.Status);
        Assert.Equal("480", result.ResponseCode);
        Assert.Equal("000777", result.Stan);
    }

    [Fact]
    public async Task CancelAsync_104Cr995_IsPending_KeepsStanAndMerchantReceipt()
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000") + F("008", "123456") + F("010", Receipt)
                : F("001", "104") + F("013", "995"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPending);
        Assert.Equal(CancellationStatus.Pending, result.Status);
        Assert.Equal("995", result.ResponseCode);
        Assert.Equal("123456", result.Stan);
        Assert.NotNull(result.MerchantReceipt);
        Assert.Null(result.CustomerReceipt);
    }

    [Fact]
    public async Task CancelAsync_Declined103_DoesNotSend004()
    {
        var (port, requests) = StartTerminal(_ =>
            F("001", "103") + F("003", "0100001") + F("013", "302"));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);
        var frames = await requests;

        Assert.False(result.IsSuccess);
        Assert.Equal(CancellationStatus.Failed, result.Status);
        Assert.Equal("302", result.ResponseCode);
        Assert.Single(frames);
    }

    [Theory]
    [InlineData("302")]
    [InlineData("992")]   // live v5.4.4: already cancelled / not cancellable
    [InlineData("909")]   // live v5.4.4: TM 004 refused (e.g. without MT)
    public async Task CancelAsync_Refused104_IsFailure(string code)
    {
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000")
                : F("001", "104") + F("013", code));

        var result = await Client(port).CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsPending);
        Assert.Equal(CancellationStatus.Failed, result.Status);
        Assert.Equal(code, result.ResponseCode);
    }

    [Fact]
    public async Task CancelAsync_No104Reply_FailsClosedAsPending()
    {
        // TM 103 CR=000 only means "found"; TM 004 executes the reversal (spec §4.4).
        // No TM 104 reply → outcome unknown → pending, never a completed void.
        var (port, _) = StartTerminal(frame =>
            TlvProtocol.Parse(frame)[TlvTags.Tm] == "003"
                ? F("001", "103") + F("013", "000") + F("008", "123456") + F("010", Receipt)
                : null);

        var result = await Client(port, TimeSpan.FromMilliseconds(500))
            .CancelAsync("123456", "01", "00001", "000042", 15000);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPending);
        Assert.Equal(CancellationStatus.Pending, result.Status);
        Assert.Equal("", result.ResponseCode);
        Assert.Equal("123456", result.Stan);           // for TM 008 verification
        Assert.NotNull(result.MerchantReceipt);
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

    // -------------------------------------------------------------------------
    // Duplicate receipt by STAN (TM 008)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateReceiptByStanAsync_SendsStan_AndReturnsReceipt()
    {
        var (port, requests) = StartTerminal(_ =>
            F("001", "108") + F("013", "000") + F("008", "124589") + F("010", Receipt));

        var result = await Client(port).DuplicateReceiptByStanAsync("124589");
        var frames = await requests;

        Assert.True(result.IsApproved());
        Assert.Equal("124589", result.Stan);
        Assert.NotNull(result.MerchantReceipt);
        var req = TlvProtocol.Parse(frames[0]);
        Assert.Equal("008", req[TlvTags.Tm]);
        Assert.Equal("124589", req[TlvTags.Stan]);
    }
}
