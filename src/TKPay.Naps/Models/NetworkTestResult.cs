namespace TKPay.Naps.Models;

/// <summary>
/// Result of a network-test / ping request (TM=009).
/// </summary>
public sealed class NetworkTestResult
{
    /// <summary>True when the terminal reported the network as reachable (RC=000).</summary>
    public bool Success { get; init; }

    /// <summary>Raw NAPS Pay response code ("000" = OK).</summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>Human-readable status message.</summary>
    public string Message { get; init; } = "";
}
