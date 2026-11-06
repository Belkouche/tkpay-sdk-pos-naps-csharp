using TKPay.Naps;
using TKPay.Naps.Models;

// Emulator on this machine (port 4444, or pass another port).
// For a real terminal: new NapsConfig(host: "192.168.1.50")
var port = args.Length > 0 ? int.Parse(args[0]) : NapsConfig.DefaultPort;
var client = new NapsPayClient(NapsConfig.Localhost(port));

var result = await client.ProcessPaymentAsync(
    new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));

if (result.IsApproved())
{
    Console.WriteLine($"Approved  STAN={result.Stan}  card={result.GetFormattedCardNumber()}");
    Console.WriteLine(result.CustomerReceipt?.ToPlainText());
}
else
{
    Console.WriteLine($"Declined [{result.ResponseCode}] {result.Error}");
}
