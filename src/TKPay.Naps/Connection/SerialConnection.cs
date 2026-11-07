using System.IO.Ports;
using System.Text;
using TKPay.Naps.Models;

namespace TKPay.Naps.Connection;

/// <summary>
/// USB serial connection to an Ingenico Lane/3000 (CDC-ACM).
///
/// Speaks the same NAPS ASCII TLV protocol as the TCP transport: the raw TLV
/// request is written without a terminator, and the response is read until the
/// resolved end-of-message terminator ('!' v2, '?' v1) or 1 second of silence.
/// </summary>
internal sealed class SerialConnection : ITerminalConnection
{
    private readonly NapsConfig _config;
    private SerialPort? _port;

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
            // --- Send raw TLV (no terminator), same framing as TCP ---
            var bytes = Encoding.UTF8.GetBytes(tlvMessage);
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

            return await ReadResponseAsync(_port.BaseStream, cts.Token, ct);
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
