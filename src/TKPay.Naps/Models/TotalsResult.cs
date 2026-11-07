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

    /// <summary>Raw DP (010) block returned by the terminal (nested TLV, unparsed), if any.</summary>
    public string? ReceiptText { get; init; }

    /// <summary>The DP (010) receipt parsed into lines, exactly as sent, if any.</summary>
    public Receipt? Receipt { get; init; }
}
