namespace TKPay.Naps.Models;

/// <summary>Outcome of a cancellation (void).</summary>
public enum CancellationStatus
{
    /// <summary>TM 104 with CR 000 or 480 — the reversal was executed.</summary>
    Completed,

    /// <summary>Refused: TM 103 or TM 104 with any other CR. Nothing was reversed.</summary>
    Failed,

    /// <summary>
    /// Outcome unknown: TM 103 found the transaction but TM 104 did not arrive
    /// (timeout after TM 004) or returned CR 995. The reversal may or may not have
    /// executed — never treat as a completed void. Verify with
    /// <see cref="NapsPayClient.DuplicateReceiptAsync"/> (TM 008) using <see cref="CancellationResult.Stan"/>.
    /// </summary>
    Pending
}

/// <summary>
/// Result of a cancellation (void) — TM=003 → 103 (transaction found), then
/// TM=004 → 104 (reversal executed).
/// </summary>
public sealed class CancellationResult
{
    /// <summary>Completed, failed, or pending (unconfirmed).</summary>
    public CancellationStatus Status { get; init; }

    /// <summary>True only when the reversal is confirmed (<see cref="CancellationStatus.Completed"/>).</summary>
    public bool IsSuccess => Status == CancellationStatus.Completed;

    /// <summary>True when the outcome is unconfirmed (<see cref="CancellationStatus.Pending"/>).</summary>
    public bool IsPending => Status == CancellationStatus.Pending;

    /// <summary>
    /// Response code of the deciding message (TM 104, or TM 103 when it refused).
    /// Empty when pending because no TM 104 reply arrived.
    /// </summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>
    /// STAN: the void STAN from TM 104 when completed; the STAN of the transaction
    /// being cancelled when pending (use it to verify via TM 008).
    /// </summary>
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
            Status = CancellationStatus.Completed, ResponseCode = responseCode, Stan = stan,
            MerchantReceipt = merchantReceipt, CustomerReceipt = customerReceipt
        };

    internal static CancellationResult Pending(string responseCode, string? stan, Receipt? merchantReceipt) =>
        new()
        {
            Status = CancellationStatus.Pending, ResponseCode = responseCode, Stan = stan,
            MerchantReceipt = merchantReceipt
        };

    internal static CancellationResult Failure(string responseCode) =>
        new() { Status = CancellationStatus.Failed, ResponseCode = responseCode };
}
