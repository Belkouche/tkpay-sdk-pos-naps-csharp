namespace TKPay.Naps.Models;

/// <summary>
/// NAPS TLV protocol version. Selects the expected end-of-message terminator
/// on the wire: v2 responses end with '!', v1 responses end with '?'.
/// </summary>
public enum ProtocolVersion
{
    /// <summary>Legacy protocol — responses terminated by '?'.</summary>
    V1,

    /// <summary>Current protocol — responses terminated by '!'.</summary>
    V2
}

/// <summary>
/// Configuration for a NAPS Pay terminal connection.
///
/// <para>
/// Two transports are supported: TCP socket (Sunmi P2/P3, the default) and
/// USB serial (Ingenico Lane/3000, CDC-ACM). Use the standard constructor for
/// TCP, or <see cref="Serial"/> for a USB serial link.
/// </para>
/// </summary>
public sealed class NapsConfig
{
    /// <summary>Default TCP port for NAPS Pay M2M protocol.</summary>
    public const int DefaultPort = 4444;

    /// <summary>Default baud rate for the Ingenico USB serial (CDC-ACM) link.</summary>
    public const int DefaultBaudRate = 115200;

    /// <summary>Default connection + read timeout: 2 minutes.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Phase-2 confirmation must be sent within 40 seconds on the same connection.</summary>
    public static readonly TimeSpan DefaultConfirmationTimeout = TimeSpan.FromSeconds(40);

    /// <summary>Transport used to reach the terminal.</summary>
    public TransportType Transport { get; }

    /// <summary>IP address or hostname of the terminal (TCP transport only).</summary>
    public string? Host { get; }

    /// <summary>TCP port (default: 4444). Unused for the serial transport.</summary>
    public int Port { get; }

    /// <summary>
    /// Serial port name/path (serial transport only). When <c>null</c>, the port
    /// is auto-discovered by USB vendor id 0x0B00 with a CDC-ACM fallback.
    /// </summary>
    public string? SerialPortName { get; }

    /// <summary>Serial baud rate (serial transport only, default: 115200).</summary>
    public int SerialBaudRate { get; }

    /// <summary>Connection and read timeout for Phase 1 (default: 2 minutes).</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Timeout for Phase 2 confirmation (default: 40 seconds).</summary>
    public TimeSpan ConfirmationTimeout { get; }

    /// <summary>
    /// TLV protocol version. Selects the expected response terminator:
    /// '!' for v2, '?' for v1. Defaults to v2 on both transports.
    /// </summary>
    public ProtocolVersion ProtocolVersion { get; }

    /// <summary>End-of-message terminator expected on the wire for this protocol version.</summary>
    public char Terminator => ProtocolVersion == ProtocolVersion.V1 ? '?' : '!';

    // -------------------------------------------------------------------------
    // TCP constructor (default transport) — unchanged public shape.
    // -------------------------------------------------------------------------

    /// <param name="host">Terminal IP address or hostname.</param>
    /// <param name="port">TCP port (default: 4444).</param>
    /// <param name="timeout">Phase-1 timeout (default: 2 minutes).</param>
    /// <param name="confirmationTimeout">Phase-2 timeout (default: 40 seconds).</param>
    /// <param name="protocolVersion">
    /// TLV protocol version. Leave <c>null</c> to resolve per transport (TCP → v2).
    /// </param>
    public NapsConfig(
        string host,
        int port = DefaultPort,
        TimeSpan? timeout = null,
        TimeSpan? confirmationTimeout = null,
        ProtocolVersion? protocolVersion = null)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be blank.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        Transport = TransportType.TcpSocket;
        Host = host;
        Port = port;
        SerialPortName = null;
        SerialBaudRate = DefaultBaudRate;
        Timeout = timeout ?? DefaultTimeout;
        ConfirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
        ProtocolVersion = protocolVersion ?? ResolveDefaultProtocol(TransportType.TcpSocket);
    }

    // -------------------------------------------------------------------------
    // Serial constructor — private, reached via the Serial() factory.
    // -------------------------------------------------------------------------

    private NapsConfig(
        TransportType transport,
        string? serialPortName,
        int serialBaudRate,
        TimeSpan? timeout,
        TimeSpan? confirmationTimeout,
        ProtocolVersion? protocolVersion)
    {
        _ = transport; // discriminator only — this ctor is always UsbSerial

        if (serialBaudRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(serialBaudRate), "Baud rate must be positive.");

        Transport = TransportType.UsbSerial;
        Host = null;
        Port = DefaultPort;
        SerialPortName = string.IsNullOrWhiteSpace(serialPortName) ? null : serialPortName;
        SerialBaudRate = serialBaudRate;
        Timeout = timeout ?? DefaultTimeout;
        ConfirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
        ProtocolVersion = protocolVersion ?? ResolveDefaultProtocol(TransportType.UsbSerial);
    }

    /// <summary>
    /// Create a config for the Ingenico Lane/3000 USB serial transport.
    /// </summary>
    /// <param name="portName">
    /// Serial port name/path. Leave <c>null</c> to auto-discover the Ingenico
    /// port (USB VID 0x0B00, with a CDC-ACM fallback).
    /// </param>
    /// <param name="baudRate">Baud rate (default: 115200).</param>
    /// <param name="timeout">Phase-1 timeout (default: 2 minutes).</param>
    /// <param name="confirmationTimeout">Phase-2 timeout (default: 40 seconds).</param>
    /// <param name="protocolVersion">
    /// TLV protocol version. Leave <c>null</c> for v2 (the Lane3600 ends every reply with '!');
    /// set v1 for a legacy terminal.
    /// </param>
    public static NapsConfig Serial(
        string? portName = null,
        int baudRate = DefaultBaudRate,
        TimeSpan? timeout = null,
        TimeSpan? confirmationTimeout = null,
        ProtocolVersion? protocolVersion = null) =>
        new(TransportType.UsbSerial, portName, baudRate, timeout, confirmationTimeout, protocolVersion);

    /// <summary>Create a config pointing to localhost (useful for tests).</summary>
    public static NapsConfig Localhost(int port = DefaultPort) => new("127.0.0.1", port);

    /// <summary>
    /// Default protocol version: v2 on both transports. The Ingenico Lane3600 over USB ends every
    /// reply with '!' and was only verified with '!'-terminated requests.
    /// </summary>
    internal static ProtocolVersion ResolveDefaultProtocol(TransportType transport) => ProtocolVersion.V2;
}
