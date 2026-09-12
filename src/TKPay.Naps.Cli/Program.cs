using TKPay.Naps;
using TKPay.Naps.Models;

// Usage: naps-pay <host> <amount> [registerId] [cashierId] [port]
var host       = args.Length > 0 ? args[0] : "127.0.0.1";
var amount     = args.Length > 1 ? decimal.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10.00m;
var registerId = args.Length > 2 ? args[2] : "01";
var cashierId  = args.Length > 3 ? args[3] : "00001";
var port       = args.Length > 4 ? int.Parse(args[4]) : NapsConfig.DefaultPort;

Console.WriteLine($"NAPS Pay CLI");
Console.WriteLine($"  Terminal : {host}:{port}");
Console.WriteLine($"  Amount   : {amount:F2} MAD");
Console.WriteLine($"  Register : {registerId}  Cashier: {cashierId}");
Console.WriteLine();

var config = new NapsConfig(host, port);
var client = new NapsPayClient(config);

Console.Write("Connecting... ");
var reachable = await client.TestConnectionAsync();
if (!reachable)
{
    Console.WriteLine($"FAILED — cannot reach {host}:{port}");
    return 1;
}
Console.WriteLine("OK");

Console.Write("Sending Phase-1 (TM=001)... ");
try
{
    var result = await client.ProcessPaymentAsync(
        new PaymentRequest(amount, registerId, cashierId));

    if (result.IsApproved())
    {
        Console.WriteLine("APPROVED");
        Console.WriteLine();
        Console.WriteLine($"  STAN       : {result.Stan}");
        Console.WriteLine($"  Card       : {result.GetFormattedCardNumber()}");
        Console.WriteLine($"  Expiry     : {result.GetFormattedExpiry()}");
        Console.WriteLine($"  Entry mode : {result.EntryMode ?? "N/A"}");
        Console.WriteLine($"  Auth #     : {result.AuthNumber ?? "N/A"}");
        Console.WriteLine($"  TX Date    : {result.TransactionDate}  {result.TransactionTime}");

        if (result.MerchantReceipt is { } mr)
        {
            Console.WriteLine();
            Console.WriteLine("── Merchant Receipt ──────────────────");
            Console.WriteLine(mr.ToPlainText());
        }
        if (result.CustomerReceipt is { } cr)
        {
            Console.WriteLine();
            Console.WriteLine("── Customer Receipt ──────────────────");
            Console.WriteLine(cr.ToPlainText());
        }
        return 0;
    }
    else
    {
        Console.WriteLine($"DECLINED  (code: {result.ResponseCode})");
        Console.WriteLine($"  {result.Error}");
        if (result.MerchantReceipt is { } dr)
        {
            Console.WriteLine();
            Console.WriteLine("── Decline Receipt ───────────────────");
            Console.WriteLine(dr.ToPlainText());
        }
        return 2;
    }
}
catch (NapsException ex)
{
    Console.WriteLine($"ERROR — {ex.Message}");
    return 3;
}
