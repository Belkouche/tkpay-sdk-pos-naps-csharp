namespace TKPay.Naps.Models;

/// <summary>
/// Terminal transport type. Selects how the SDK reaches the terminal and,
/// unless overridden, the default protocol version for that link.
/// </summary>
public enum TransportType
{
    /// <summary>Sunmi P2/P3 via TCP socket (default). Defaults to protocol v2 ('!').</summary>
    TcpSocket,

    /// <summary>Ingenico Lane/3000 via USB serial (CDC-ACM). Defaults to protocol v1 ('?').</summary>
    UsbSerial
}
