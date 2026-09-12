namespace TKPay.Naps.Models;

/// <summary>
/// Result of a totals-report request (TM=010).
/// </summary>
public sealed class TotalsResult
{
    /// <summary>True when the terminal returned totals (RC=000).</summary>
    public bool Success { get; init; }

    /// <summary>Raw NAPS Pay response code ("000" = OK).</summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>Receipt / printable totals text (DP/010) returned by the terminal, if any.</summary>
    public string? ReceiptText { get; init; }
}
