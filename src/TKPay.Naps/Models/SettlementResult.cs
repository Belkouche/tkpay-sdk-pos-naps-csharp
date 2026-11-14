using TKPay.Naps.Protocol;

namespace TKPay.Naps.Models;

/// <summary>
/// Result of the deprecated <see cref="NapsPayClient.SettlementAsync"/>: always
/// <c>"UNSUPPORTED"</c>, as the ECR protocol has no telecollecte message.
/// </summary>
public sealed class SettlementResult
{
    /// <summary>Whether the NAPS server accepted the settlement.</summary>
    public bool Success { get; }

    /// <summary>Raw NAPS Pay response code ("000" = accepted).</summary>
    public string ResponseCode { get; }

    /// <summary>Settlement date in DDMMYYYY format, if returned.</summary>
    public string? Date { get; }

    /// <summary>Settlement time in HHMMSS format, if returned.</summary>
    public string? Time { get; }

    /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; }

    /// <summary>The DP (010) receipt returned by the terminal, exactly as sent, if any.</summary>
    public Receipt? Receipt { get; }

    internal SettlementResult(
        bool success,
        string responseCode,
        string? date = null,
        string? time = null,
        string? error = null,
        Receipt? receipt = null)
    {
        Success = success;
        ResponseCode = responseCode;
        Date = date;
        Time = time;
        Error = error;
        Receipt = receipt;
    }

    internal static SettlementResult Unsupported() =>
        new(success: false, responseCode: "UNSUPPORTED", error: "Telecollecte is not part of the NapsPay ECR protocol: run it from the terminal menu. Use TotalsAsync() for the daily totals ticket.");
}
