namespace TKPay.Naps.Models;

/// <summary>
/// Configuration for NAPS Pay M2M TCP connection.
/// </summary>
public sealed class NapsConfig
{
    /// <summary>Default TCP port for NAPS Pay M2M protocol.</summary>
    public const int DefaultPort = 4444;

    /// <summary>Default connection + read timeout: 2 minutes.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Phase-2 confirmation must be sent within 40 seconds on the same connection.</summary>
    public static readonly TimeSpan DefaultConfirmationTimeout = TimeSpan.FromSeconds(40);

    /// <summary>IP address or hostname of the NAPS Pay terminal.</summary>
    public string Host { get; }

    /// <summary>TCP port (default: 4444).</summary>
    public int Port { get; }

    /// <summary>Connection and read timeout for Phase 1 (default: 2 minutes).</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Timeout for Phase 2 confirmation (default: 40 seconds).</summary>
    public TimeSpan ConfirmationTimeout { get; }

    /// <param name="host">Terminal IP address or hostname.</param>
    /// <param name="port">TCP port (default: 4444).</param>
    /// <param name="timeout">Phase-1 timeout (default: 2 minutes).</param>
    /// <param name="confirmationTimeout">Phase-2 timeout (default: 40 seconds).</param>
    public NapsConfig(
        string host,
        int port = DefaultPort,
        TimeSpan? timeout = null,
        TimeSpan? confirmationTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be blank.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        Host = host;
        Port = port;
        Timeout = timeout ?? DefaultTimeout;
        ConfirmationTimeout = confirmationTimeout ?? DefaultConfirmationTimeout;
    }

    /// <summary>Create a config pointing to localhost (useful for tests).</summary>
    public static NapsConfig Localhost(int port = DefaultPort) => new("127.0.0.1", port);
}
