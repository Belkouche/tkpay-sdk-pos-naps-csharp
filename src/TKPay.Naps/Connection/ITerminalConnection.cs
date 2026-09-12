using TKPay.Naps.Models;

namespace TKPay.Naps.Connection;

/// <summary>
/// A connection to a NAPS Pay terminal — TCP socket or USB serial. The connection
/// is kept open across Phase-1 and Phase-2 of a payment (Phase-2 must arrive within
/// 40 seconds on the same connection).
/// </summary>
internal interface ITerminalConnection : IAsyncDisposable
{
    /// <summary>True while the link is open.</summary>
    bool IsConnected { get; }

    /// <summary>Open the link to the terminal.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Close the link and release resources.</summary>
    Task DisconnectAsync();

    /// <summary>Write a TLV message and read the full response until the terminator.</summary>
    Task<string> SendAndReceiveAsync(string tlvMessage, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Factory for the terminal connection matching a config's transport.</summary>
internal static class TerminalConnection
{
    /// <summary>Create the connection for a config's transport.</summary>
    internal static ITerminalConnection Create(NapsConfig config) => config.Transport switch
    {
        TransportType.TcpSocket => new NapsConnection(config),
        TransportType.UsbSerial => new SerialConnection(config),
        _ => throw new ArgumentException($"Unknown transport: {config.Transport}", nameof(config))
    };
}
