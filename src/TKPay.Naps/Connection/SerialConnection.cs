using System.IO.Ports;
using System.Text;
using TKPay.Naps.Models;

namespace TKPay.Naps.Connection;

/// <summary>
/// USB serial connection to an Ingenico Lane/3000 (CDC-ACM).
///
/// Speaks the same NAPS ASCII TLV protocol as the TCP transport. Each request ends
/// with the resolved terminator ('!' v2, '?' v1), and the response is read until a
/// terminator or 1 second of silence.
/// </summary>
internal sealed class SerialConnection : ITerminalConnection
{
    /// <summary>
    /// The Ingenico Lane3600 holds back every answer until ~30 s after the port opens: the
    /// first frame's reply always landed at 30.1 s. A TM 101 delayed that way reached us after
    /// the terminal had given up waiting for TM 002 ("attente validation caisse" timeout), so
    /// the payment was never recorded. Frames sent once the port has been open this long answer
    /// normally, so the first frame waits for it. Keep the client connected between
    /// transactions to pay this once.
    /// </summary>
    internal static readonly TimeSpan ReadyDelay = TimeSpan.FromSeconds(32);

    /// <summary>
    /// Quiet time kept between the terminal's last byte and our next frame. On the Lane3600 a
    /// TM 003 written the instant TM 102 arrived was dropped (no reply, void never started);
    /// the same frame sent seconds later completed. The Node SDK's 0.5 s works there.
    /// </summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly NapsConfig _config;
    private SerialPort? _port;
    private DateTime _openedAtUtc;
    private DateTime _lastRxUtc;

    internal SerialConnection(NapsConfig config)
    {
        _config = config;
    }

    /// <summary>True while the serial port is open.</summary>
    public bool IsConnected => _port?.IsOpen == true;

    /// <summary>Open the serial port, auto-discovering the Ingenico port if none is configured.</summary>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        DisposePort();

        var portName = _config.SerialPortName ?? PortDiscovery.FindIngenicoPort();
        if (portName == null)
            throw NapsException.ConnectionFailed(
                new InvalidOperationException("Ingenico Lane/3000 not found. Check the USB connection."));

        try
        {
            _port = new SerialPort(portName, _config.SerialBaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 50,
                WriteTimeout = (int)_config.Timeout.TotalMilliseconds,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };
            _port.Open();
            _openedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not NapsException)
        {
            DisposePort();
            throw NapsException.ConnectionFailed(ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>Close the serial port and release resources.</summary>
    public Task DisconnectAsync()
    {
        DisposePort();
        return Task.CompletedTask;
    }

    /// <summary>Write the TLV message and read the response until the terminator (or silence).</summary>
    public async Task<string> SendAndReceiveAsync(
        string tlvMessage,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (_port is not { IsOpen: true })
            throw NapsException.ConnectionFailed(new InvalidOperationException("Not connected."));

        try
        {
            var wait = RemainingWait(_openedAtUtc, _lastRxUtc, DateTime.UtcNow);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            // --- Send TLV + terminator. The Lane3600 dropped an unterminated TM 003 sent while
            // it was still finishing a sale (no reply, void timed out); terminated frames, as
            // the Node SDK sends, are taken in that state too. ---
            var frame = tlvMessage.EndsWith(_config.Terminator.ToString())
                ? tlvMessage
                : tlvMessage + _config.Terminator;
            var bytes = Encoding.UTF8.GetBytes(frame);
            _port.DiscardInBuffer();
#if NET5_0_OR_GREATER
            await _port.BaseStream.WriteAsync(bytes, ct);
#else
            await _port.BaseStream.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
            await _port.BaseStream.FlushAsync(ct);

            // --- Receive ---
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var response = await ReadResponseAsync(_port.BaseStream, cts.Token, ct);
            _lastRxUtc = DateTime.UtcNow;
            return response;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw NapsException.Timeout(ex);
        }
        catch (Exception ex) when (ex is not NapsException)
        {
            throw NapsException.ConnectionFailed(ex);
        }
    }

    /// <summary>
    /// Time to wait before writing: until the port has been open <see cref="ReadyDelay"/>, and
    /// <see cref="SettleDelay"/> has passed since the terminal's last reply.
    /// </summary>
    internal static TimeSpan RemainingWait(DateTime openedAtUtc, DateTime lastRxUtc, DateTime nowUtc)
    {
        var readyAt = openedAtUtc + ReadyDelay;
        var settledAt = lastRxUtc + SettleDelay;
        var left = (readyAt > settledAt ? readyAt : settledAt) - nowUtc;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Read from the serial stream until an end-of-message terminator ('!' or '?',
    /// both accepted regardless of the configured version), or until 1 second of
    /// silence after the first chunk (fallback for partial frames). Response is
    /// UTF-8; length fields count chars.
    /// </summary>
    private static async Task<string> ReadResponseAsync(
        Stream stream, CancellationToken outerCt, CancellationToken userCt)
    {
        static bool IsTerminator(byte b) => b == (byte)'!' || b == (byte)'?';

        const int bufSize = 4096;
        var rawBytes = new List<byte>(bufSize);
        var buf = new byte[bufSize];

        // First read — block until data arrives (outer timeout via outerCt).
#if NET5_0_OR_GREATER
        var count = await stream.ReadAsync(buf, outerCt);
#else
        var count = await stream.ReadAsync(buf, 0, buf.Length, outerCt);
#endif
        if (count == 0)
            throw NapsException.InvalidResponse("Connection closed before response.");

        rawBytes.AddRange(buf.Take(count));
        var done = IsTerminator(buf[count - 1]);

        // Drain remaining chunks with a 1-second inter-chunk silence fallback.
        while (!done)
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            drainCts.CancelAfter(TimeSpan.FromSeconds(1));

            try
            {
#if NET5_0_OR_GREATER
                count = await stream.ReadAsync(buf, drainCts.Token);
#else
                count = await stream.ReadAsync(buf, 0, buf.Length, drainCts.Token);
#endif
                if (count == 0) break;
                rawBytes.AddRange(buf.Take(count));
                done = IsTerminator(buf[count - 1]);
            }
            catch (OperationCanceledException) when (!userCt.IsCancellationRequested)
            {
                break; // 1-second silence — treat as end-of-message
            }
        }

        if (rawBytes.Count == 0)
            throw NapsException.InvalidResponse("Empty response from terminal.");

        var result = Encoding.UTF8.GetString(rawBytes.ToArray());

        // The terminator ('!' v2, '?' v1), when present, is left in place: TlvProtocol.Parse
        // stops at it on a field boundary. Stripping by character could cut a frame sent
        // without one (TM 103/104) whose receipt text contains or ends with '!' or '?'.
        return result;
    }

    public ValueTask DisposeAsync()
    {
        DisposePort();
        return default;
    }

    private void DisposePort()
    {
        if (_port is null) return;
        try { if (_port.IsOpen) _port.Close(); } catch { /* port may already be dead */ }
        try { _port.Dispose(); } catch { /* ignore */ }
        _port = null;
    }
}
