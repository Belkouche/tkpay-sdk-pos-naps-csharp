namespace TKPay.Naps.Models;

/// <summary>
/// Result of a TM=010 settlement (telecollecte / end-of-day batch).
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

    internal SettlementResult(
        bool success,
        string responseCode,
        string? date = null,
        string? time = null,
        string? error = null)
    {
        Success = success;
        ResponseCode = responseCode;
        Date = date;
        Time = time;
        Error = error;
    }

    internal static SettlementResult Accepted(IReadOnlyDictionary<string, string> fields) =>
        new(
            success: true,
            responseCode: fields.GetValueOrDefault(TlvTags.CR, "000"),
            date: fields.GetValueOrDefault(TlvTags.Da),
            time: fields.GetValueOrDefault(TlvTags.He)
        );

    internal static SettlementResult Declined(string responseCode) =>
        new(
            success: false,
            responseCode: responseCode,
            error: responseCode switch
            {
                "052" => "Already settled today",
                "909" => "Terminal or server is down",
                _     => $"Settlement failed — code: {responseCode}"
            }
        );
}
