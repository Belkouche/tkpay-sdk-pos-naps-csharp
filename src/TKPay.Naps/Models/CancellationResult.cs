namespace TKPay.Naps.Models;

/// <summary>
/// Result of a cancellation request (TM=003).
/// </summary>
public sealed class CancellationResult
{
    /// <summary>True when MT=103 or MT=104 and RC=000.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Response code returned by the terminal (000 = success).</summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>STAN echoed by the terminal, if present.</summary>
    public string? Stan { get; init; }

    internal static CancellationResult Success(string responseCode, string? stan) =>
        new() { IsSuccess = true, ResponseCode = responseCode, Stan = stan };

    internal static CancellationResult Failure(string responseCode) =>
        new() { IsSuccess = false, ResponseCode = responseCode };
}
