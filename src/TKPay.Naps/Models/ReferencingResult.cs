namespace TKPay.Naps.Models;

/// <summary>
/// Result of a referencing request (TM=013) — loading a terminal parameter set.
/// </summary>
public sealed class ReferencingResult
{
    /// <summary>True when the terminal accepted the parameter load (RC=000).</summary>
    public bool Success { get; init; }

    /// <summary>Raw NAPS Pay response code ("000" = accepted).</summary>
    public string ResponseCode { get; init; } = "";

    /// <summary>The TIDE (020) parameter set that was requested: "01" merchant, "02" recharge.</summary>
    public string? ParameterSet { get; init; }

    /// <summary>Raw DP (010) block returned by the terminal (nested TLV, unparsed), if any.</summary>
    public string? ReceiptText { get; init; }

    /// <summary>The DP (010) receipt parsed into lines, exactly as sent, if any.</summary>
    public Receipt? Receipt { get; init; }
}
