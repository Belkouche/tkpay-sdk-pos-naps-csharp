using TKPay.Naps; // TlvTags

namespace TKPay.Naps.Models;

/// <summary>
/// Result of a completed NAPS Pay transaction.
/// </summary>
public sealed class PaymentResult
{
    /// <summary>Whether the terminal approved the transaction.</summary>
    public bool Success { get; }

    /// <summary>Raw NAPS Pay response code ("000" = approved).</summary>
    public string ResponseCode { get; }

    /// <summary>System Trace Audit Number — use for reversals.</summary>
    public string? Stan { get; }

    /// <summary>
    /// Masked card number — first 6 + last 4 digits only (PCI DSS).
    /// Example: <c>516794******3315</c>
    /// </summary>
    public string? MaskedCardNumber { get; }

    /// <summary>Card expiry in YYMM format (e.g. "3010" = Oct 2030).</summary>
    public string? CardExpiry { get; }

    /// <summary>Cardholder name, if returned by terminal.</summary>
    public string? CardholderName { get; }

    /// <summary>Entry mode (e.g. "CC" = Contactless, "SC" = Chip).</summary>
    public string? EntryMode { get; }

    /// <summary>Authorization number from acquirer.</summary>
    public string? AuthNumber { get; }

    /// <summary>NCAI (register + cashier) echoed by terminal.</summary>
    public string? Ncai { get; }

    /// <summary>Sequence number used in the transaction.</summary>
    public string? Sequence { get; }

    /// <summary>Transaction date in DDMMYYYY format.</summary>
    public string? TransactionDate { get; }

    /// <summary>Transaction time in HHMMSS format.</summary>
    public string? TransactionTime { get; }

    /// <summary>Merchant copy of the receipt.</summary>
    public Receipt? MerchantReceipt { get; }

    /// <summary>Customer copy of the receipt.</summary>
    public Receipt? CustomerReceipt { get; }

    /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; }

    internal PaymentResult(
        bool success,
        string responseCode,
        string? stan = null,
        string? maskedCardNumber = null,
        string? cardExpiry = null,
        string? cardholderName = null,
        string? entryMode = null,
        string? authNumber = null,
        string? ncai = null,
        string? sequence = null,
        string? transactionDate = null,
        string? transactionTime = null,
        Receipt? merchantReceipt = null,
        Receipt? customerReceipt = null,
        string? error = null)
    {
        Success = success;
        ResponseCode = responseCode;
        Stan = stan;
        MaskedCardNumber = maskedCardNumber;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        EntryMode = entryMode;
        AuthNumber = authNumber;
        Ncai = ncai;
        Sequence = sequence;
        TransactionDate = transactionDate;
        TransactionTime = transactionTime;
        MerchantReceipt = merchantReceipt;
        CustomerReceipt = customerReceipt;
        Error = error;
    }

    /// <summary>True when the terminal approved (response code "000").</summary>
    public bool IsApproved() => Success && ResponseCode == "000";

    /// <summary>
    /// Formatted card number for display.
    /// Returns <c>"N/A"</c> when not available.
    /// </summary>
    public string GetFormattedCardNumber() => MaskedCardNumber ?? "N/A";

    /// <summary>
    /// Formatted expiry for display (MM/YY).
    /// Input YYMM "3010" → "10/30".
    /// Returns <c>"N/A"</c> when not available.
    /// </summary>
    public string GetFormattedExpiry()
    {
        if (CardExpiry is { Length: 4 } e)
            return $"{e[2..4]}/{e[0..2]}";
        return CardExpiry ?? "N/A";
    }

    // -------------------------------------------------------------------------
    // Internal factories
    // -------------------------------------------------------------------------

    internal static PaymentResult Approved(
        IReadOnlyDictionary<string, string> fields,
        Receipt? merchantReceipt,
        Receipt? customerReceipt) =>
        new(
            success: true,
            responseCode: fields.GetValueOrDefault(TlvTags.CR, "000"),
            stan: fields.GetValueOrDefault(TlvTags.Stan),
            maskedCardNumber: fields.GetValueOrDefault(TlvTags.Ncar),
            cardExpiry: fields.GetValueOrDefault(TlvTags.Daex),
            cardholderName: fields.GetValueOrDefault(TlvTags.Nprt),
            entryMode: fields.GetValueOrDefault(TlvTags.Em),
            authNumber: fields.GetValueOrDefault(TlvTags.Na),
            ncai: fields.GetValueOrDefault(TlvTags.Ncai),
            sequence: fields.GetValueOrDefault(TlvTags.Ns),
            transactionDate: fields.GetValueOrDefault(TlvTags.Datr),
            transactionTime: fields.GetValueOrDefault(TlvTags.Hetr),
            merchantReceipt: merchantReceipt,
            customerReceipt: customerReceipt
        );

    internal static PaymentResult Failed(string responseCode, IReadOnlyDictionary<string, string> fields) =>
        new(
            success: false,
            responseCode: responseCode,
            stan: fields.GetValueOrDefault(TlvTags.Stan),
            error: responseCode switch
            {
                "909" => "Terminal or server is down",
                "302" => "Transaction not found",
                "482" => "Transaction already cancelled",
                "480" => "Transaction cancelled",
                _     => $"Payment declined — code: {responseCode}"
            }
        );
}
