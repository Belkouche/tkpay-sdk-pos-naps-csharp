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
internal sealed class NapsConnection : ITerminalConnection
{
    private readonly NapsConfig _config;
    private TcpClient?    _tcp;
    private NetworkStream? _stream;

    internal NapsConnection(NapsConfig config)
    {
        _config = config;
    }

    /// <summary>True while the socket is open and connected.</summary>
    public bool IsConnected =>
        _tcp is { Connected: true } && _stream is not null;

    // -------------------------------------------------------------------------
    // Connect / Disconnect
    // -------------------------------------------------------------------------

    /// <summary>Open a TCP connection to the terminal.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await DisposeSocketAsync();

        if (_config.Host is null)
            throw NapsException.ConnectionFailed(
                new InvalidOperationException("Host must be set for the TCP transport."));

        _tcp = new TcpClient
        {
            ReceiveTimeout = (int)_config.Timeout.TotalMilliseconds,
            SendTimeout    = (int)_config.Timeout.TotalMilliseconds
        };

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_config.Timeout);

#if NET5_0_OR_GREATER
            await _tcp.ConnectAsync(_config.Host, _config.Port, connectCts.Token);
#else
            // net48 TcpClient has no cancellable ConnectAsync; race the connect
            // against the linked timeout token so the same timeout behaviour holds.
            var connectTask = _tcp.ConnectAsync(_config.Host, _config.Port);
            var completed = await Task.WhenAny(
                connectTask, Task.Delay(Timeout.Infinite, connectCts.Token));
            if (completed != connectTask)
                connectCts.Token.ThrowIfCancellationRequested();
            await connectTask;
#endif
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
    public async Task DisconnectAsync()
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
    public async Task<string> SendAndReceiveAsync(
        string tlvMessage,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (!IsConnected)
            throw NapsException.ConnectionFailed(new InvalidOperationException("Not connected."));

        try
        {
            // --- Send ---
            var bytes = Encoding.UTF8.GetBytes(tlvMessage);
#if NET5_0_OR_GREATER
            await _stream!.WriteAsync(bytes, ct);
#else
            await _stream!.WriteAsync(bytes, 0, bytes.Length, ct);
#endif
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
    /// Read from the stream until the end-of-message terminator is received
    /// ('!' for protocol v2, '?' for v1 — both are always accepted regardless of
    /// the configured version), or until 1 second of silence after the first chunk
    /// (fallback for partial frames). Response is UTF-8; length fields count chars.
    /// </summary>
    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        // Accept either terminator on the wire so the reader tolerates a mismatch
        // between the configured version and what the terminal actually sends.
        static bool IsTerminator(byte b) => b == (byte)'!' || b == (byte)'?';

        const int bufSize = 4096;
        var rawBytes = new List<byte>(bufSize);
        var buf = new byte[bufSize];

        // First read — block until data arrives (outer timeout via ct)
#if NET5_0_OR_GREATER
        var count = await stream.ReadAsync(buf, ct);
#else
        var count = await stream.ReadAsync(buf, 0, buf.Length, ct);
#endif
        if (count == 0)
            throw NapsException.InvalidResponse("Connection closed before response.");

        rawBytes.AddRange(buf.Take(count));
        var done = IsTerminator(buf[count - 1]);

        // Drain remaining chunks with a 1-second inter-chunk gap
        while (!done)
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break; // 1-second silence — treat as end-of-message
            }
        }

        if (rawBytes.Count == 0)
            throw NapsException.InvalidResponse("Empty response from terminal.");

        // Decode as UTF-8 so char-based TLV length fields align with string indices
        var result = Encoding.UTF8.GetString(rawBytes.ToArray());

        // Strip the trailing terminator ('!' v2, '?' v1) if present
        var termPos = result.LastIndexOfAny(new[] { '!', '?' });
        return termPos >= 0 ? result[..termPos] : result;
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
#if NET5_0_OR_GREATER
            try { await _stream.DisposeAsync(); } catch { /* ignore */ }
#else
            try { _stream.Dispose(); } catch { /* ignore */ }
            await Task.CompletedTask;
#endif
            _stream = null;
        }

        if (_tcp is not null)
        {
            try { _tcp.Dispose(); } catch { /* ignore */ }
            _tcp = null;
        }
    }
}
