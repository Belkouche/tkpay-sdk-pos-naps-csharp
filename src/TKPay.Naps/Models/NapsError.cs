namespace TKPay.Naps.Models;

/// <summary>Error codes thrown by the SDK.</summary>
public enum NapsErrorCode
{
    ConnectionFailed,
    Timeout,
    PaymentDeclined,
    InvalidResponse,
    TerminalDown,
    TransactionNotFound,
    AlreadyCancelled,
    UnknownError
}

/// <summary>
/// Exception thrown by <see cref="NapsPayClient"/> on protocol or connectivity errors.
/// </summary>
public sealed class NapsException : Exception
{
    /// <summary>Structured error code.</summary>
    public NapsErrorCode Code { get; }

    public NapsException(NapsErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    internal static NapsException ConnectionFailed(Exception? inner = null) =>
        new(NapsErrorCode.ConnectionFailed, "Failed to connect to NAPS Pay terminal.", inner);

    internal static NapsException Timeout(Exception? inner = null) =>
        new(NapsErrorCode.Timeout, "Request timed out.", inner);

    internal static NapsException InvalidResponse(string detail) =>
        new(NapsErrorCode.InvalidResponse, $"Invalid response from terminal: {detail}");

    internal static NapsException TerminalDown() =>
        new(NapsErrorCode.TerminalDown, "Terminal or server is down.");

    internal static NapsException TransactionNotFound() =>
        new(NapsErrorCode.TransactionNotFound, "Transaction not found.");

    internal static NapsException AlreadyCancelled() =>
        new(NapsErrorCode.AlreadyCancelled, "Transaction already cancelled.");

    internal static NapsException ForResponseCode(string code) => code switch
    {
        "909" => TerminalDown(),
        "302" => TransactionNotFound(),
        "482" => AlreadyCancelled(),
        "480" => new(NapsErrorCode.PaymentDeclined, "Transaction cancelled."),
        _     => new(NapsErrorCode.PaymentDeclined, $"Payment declined — code: {code}")
    };
}
