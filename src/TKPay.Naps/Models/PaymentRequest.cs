namespace TKPay.Naps.Models;

/// <summary>
/// Describes a payment transaction to send to the NAPS Pay terminal.
/// </summary>
public sealed class PaymentRequest
{
    /// <summary>Amount in MAD (Moroccan Dirham). Converted to centimes internally.</summary>
    public decimal Amount { get; }

    /// <summary>Register / POS identifier — exactly 2 digits (e.g. "01").</summary>
    public string RegisterId { get; }

    /// <summary>Cashier identifier — exactly 5 digits (e.g. "00001").</summary>
    public string CashierId { get; }

    /// <summary>
    /// Optional 6-digit sequence number. Auto-generated if <c>null</c>.
    /// </summary>
    public string? Sequence { get; }

    /// <param name="amount">Amount in MAD — must be positive.</param>
    /// <param name="registerId">2-digit register ID.</param>
    /// <param name="cashierId">5-digit cashier ID.</param>
    /// <param name="sequence">Optional 6-digit sequence (auto-generated if omitted).</param>
    public PaymentRequest(
        decimal amount,
        string registerId,
        string cashierId,
        string? sequence = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive.", nameof(amount));
        if (registerId.Length != 2)
            throw new ArgumentException("Register ID must be exactly 2 digits.", nameof(registerId));
        if (cashierId.Length != 5)
            throw new ArgumentException("Cashier ID must be exactly 5 digits.", nameof(cashierId));
        if (sequence is not null && sequence.Length != 6)
            throw new ArgumentException("Sequence must be exactly 6 digits.", nameof(sequence));

        Amount = amount;
        RegisterId = registerId;
        CashierId = cashierId;
        Sequence = sequence;
    }

    /// <summary>NCAI = RegisterId + CashierId (7 characters total).</summary>
    internal string GetNcai() => RegisterId + CashierId;
}
