using System.Net.Sockets;
using System.Text;
using TKPay.Naps.Models;

namespace TKPay.Naps.Connection;

/// <summary>
/// Manages the TCP socket connection to a NAPS Pay terminal.
///
/// The connection is kept open across Phase-1 and Phase-2 of a payment —
/// Phase-2 confirmation must arrive within 40 seconds on the same connection.
///
/// Typical usage:
/// <code>
/// await using var conn = new NapsConnection(config);
/// await conn.ConnectAsync(ct);
/// var response1 = await conn.SendAndReceiveAsync(tlv1, config.Timeout, ct);
/// var response2 = await conn.SendAndReceiveAsync(tlv2, config.ConfirmationTimeout, ct);
/// </code>
/// </summary>
internal sealed class NapsConnection : IAsyncDisposable
{
    private readonly NapsConfig _config;
    private TcpClient?    _tcp;
    private NetworkStream? _stream;

    internal NapsConnection(NapsConfig config)
    {
        _config = config;
    }

    /// <summary>True while the socket is open and connected.</summary>
    internal bool IsConnected =>
        _tcp is { Connected: true } && _stream is not null;

    // -------------------------------------------------------------------------
    // Connect / Disconnect
    // -------------------------------------------------------------------------

    /// <summary>Open a TCP connection to the terminal.</summary>
    internal async Task ConnectAsync(CancellationToken ct = default)
    {
        await DisposeSocketAsync();

        _tcp = new TcpClient
        {
            ReceiveTimeout = (int)_config.Timeout.TotalMilliseconds,
            SendTimeout    = (int)_config.Timeout.TotalMilliseconds
        };

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_config.Timeout);

            await _tcp.ConnectAsync(_config.Host, _config.Port, connectCts.Token);
            _stream = _tcp.GetStream();
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            await DisposeSocketAsync();
            throw NapsException.ConnectionFailed(ex);
        }
        catch (Exception ex) when (ex is not NapsException)
        {
            await DisposeSocketAsync();
            throw NapsException.ConnectionFailed(ex);
        }
    }

    /// <summary>Close the socket and release resources.</summary>
    internal async Task DisconnectAsync()
    {
        await DisposeSocketAsync();
    }

    // -------------------------------------------------------------------------
    // Send / Receive
    // -------------------------------------------------------------------------

    /// <summary>
    /// Write <paramref name="tlvMessage"/> to the socket and read the full response.
    /// </summary>
    /// <param name="tlvMessage">TLV-encoded string to send.</param>
    /// <param name="timeout">Read timeout for this exchange.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>TLV response string from terminal.</returns>
    internal async Task<string> SendAndReceiveAsync(
        string tlvMessage,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (!IsConnected)
            throw NapsException.ConnectionFailed(new InvalidOperationException("Not connected."));

        try
        {
            // --- Send ---
            var bytes = Encoding.ASCII.GetBytes(tlvMessage);
            await _stream!.WriteAsync(bytes, ct);
            await _stream.FlushAsync(ct);

            // --- Receive ---
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            return await ReadResponseAsync(_stream, cts.Token);
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

    // -------------------------------------------------------------------------
    // Read helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Read from the stream until 1 second of silence (no more bytes).
    /// NAPS Pay keeps the connection open after sending a response, so we
    /// detect end-of-message by a short read gap rather than a delimiter.
    /// </summary>
    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        const int bufSize = 8192;
        var buf = new byte[bufSize];
        var sb  = new StringBuilder();

        // First read — block until data arrives (honours the outer timeout via ct)
        var count = await stream.ReadAsync(buf, ct);
        if (count == 0)
            throw NapsException.InvalidResponse("Connection closed before response.");

        sb.Append(Encoding.ASCII.GetString(buf, 0, count));

        // Drain any remaining chunks with a short 1-second inter-chunk gap
        if (count == bufSize)
        {
            while (true)
            {
                using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                drainCts.CancelAfter(TimeSpan.FromSeconds(1));

                try
                {
                    count = await stream.ReadAsync(buf, drainCts.Token);
                    if (count == 0) break;
                    sb.Append(Encoding.ASCII.GetString(buf, 0, count));
                    if (count < bufSize) break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    break; // 1-second gap — we have the full message
                }
            }
        }

        if (sb.Length == 0)
            throw NapsException.InvalidResponse("Empty response from terminal.");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await DisposeSocketAsync();
    }

    private async Task DisposeSocketAsync()
    {
        if (_stream is not null)
        {
            try { await _stream.DisposeAsync(); } catch { /* ignore */ }
            _stream = null;
        }

        if (_tcp is not null)
        {
            try { _tcp.Dispose(); } catch { /* ignore */ }
            _tcp = null;
        }
    }
}
