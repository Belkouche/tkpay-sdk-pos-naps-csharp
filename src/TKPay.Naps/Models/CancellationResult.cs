namespace TKPay.Naps.Models;

/// <summary>
/// Result of a cancellation (void) — TM=003 → 103, then TM=004 → 104.
/// </summary>
public sealed class CancellationResult
{
    /// <summary>True when the void was accepted (RC=000).</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Response code returned by the terminal (000 = success).</summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>STAN echoed by the terminal (the void's STAN from TM 104 when available).</summary>
    public string? Stan { get; init; }

    /// <summary>Merchant copy of the void receipt (DP of TM 103).</summary>
    public Receipt? MerchantReceipt { get; init; }

    /// <summary>Customer copy of the void receipt (DP of TM 104).</summary>
    public Receipt? CustomerReceipt { get; init; }

    internal static CancellationResult Success(
        string responseCode, string? stan,
        Receipt? merchantReceipt = null, Receipt? customerReceipt = null) =>
        new()
        {
            IsSuccess = true, ResponseCode = responseCode, Stan = stan,
            MerchantReceipt = merchantReceipt, CustomerReceipt = customerReceipt
        };

    internal static CancellationResult Failure(string responseCode) =>
        new() { IsSuccess = false, ResponseCode = responseCode };
}
