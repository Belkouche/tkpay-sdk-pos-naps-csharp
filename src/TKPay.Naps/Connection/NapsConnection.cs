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
            var bytes = Encoding.UTF8.GetBytes(tlvMessage);
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
    /// Read from the stream until the '?' end-of-message terminator is received.
    /// The NAPS terminal terminates every response with '?'.
    /// Response is UTF-8 encoded; length fields count Unicode characters.
    /// </summary>
    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        const int bufSize = 4096;
        var rawBytes = new List<byte>(bufSize);
        var buf = new byte[bufSize];

        while (true)
        {
            var count = await stream.ReadAsync(buf, ct);
            if (count == 0)
                break;

            rawBytes.AddRange(buf[..count]);

            // '?' (0x3F) as the last byte signals end-of-message
            if (buf[count - 1] == (byte)'?')
                break;
        }

        if (rawBytes.Count == 0)
            throw NapsException.InvalidResponse("Empty response from terminal.");

        // Decode as UTF-8 so char-based TLV length fields align with string indices
        var result = Encoding.UTF8.GetString(rawBytes.ToArray());

        // Strip the trailing '?' terminator
        var qPos = result.LastIndexOf('?');
        return qPos >= 0 ? result[..qPos] : result;
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
