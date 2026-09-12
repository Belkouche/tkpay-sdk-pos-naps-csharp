# TKPay NAPS POS SDK — C#

C# SDK for integrating NAPS Pay terminals via the M2M TLV/TCP protocol (port 4444).

- .NET 6+ / .NET Standard compatible
- Fully `async/await` — no blocking calls
- PCI DSS compliant — PAN masked immediately on parse (first 6 + last 4 only)
- Two-phase payment flow handled automatically
- Receipt parsing with TKpay branding

---

## Installation

### From source

```bash
git clone https://github.com/Belkouche/tkpay-sdk-pos-csharp.git
```

Add as a project reference:

```xml
<ProjectReference Include="../tkpay-sdk-pos-csharp/src/TKPay.Naps/TKPay.Naps.csproj" />
```

---

## Quick Start

```csharp
using TKPay.Naps;
using TKPay.Naps.Models;

var config = new NapsConfig(host: "192.168.1.100");
var client = new NapsPayClient(config);

var result = await client.ProcessPaymentAsync(
    new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));

if (result.IsApproved())
{
    Console.WriteLine($"Approved!");
    Console.WriteLine($"STAN:  {result.Stan}");
    Console.WriteLine($"Card:  {result.GetFormattedCardNumber()}");   // 516794******3315
    Console.WriteLine($"Auth:  {result.AuthNumber}");
    Console.WriteLine($"Entry: {result.EntryMode}");

    Console.WriteLine(result.MerchantReceipt?.ToPlainText());
}
else
{
    Console.WriteLine($"Declined [{result.ResponseCode}]: {result.Error}");
}
```

---

## Configuration

```csharp
var config = new NapsConfig(
    host:                 "192.168.1.100",          // Terminal IP
    port:                 4444,                      // Default: 4444
    timeout:              TimeSpan.FromMinutes(2),   // Phase-1 timeout
    confirmationTimeout:  TimeSpan.FromSeconds(40),  // Phase-2 timeout
    protocolVersion:      ProtocolVersion.V2         // Default: V2
);
```

---

## Protocol version (v1 / v2)

The SDK speaks the NAPS TLV **v2** protocol by default (`ProtocolVersion.V2`), used
by TCP/Sunmi terminals. The version selects the end-of-message terminator expected on
the wire — `'!'` for v2, `'?'` for v1 — though the parser and socket reader tolerate
either terminator regardless of the configured version.

Under v2, the Phase-2 **Payment Confirmation (TM 002)** echoes coherence fields from
the approved Phase-1 response so the terminal can cross-check the frame it confirms:
the amount (MT + DE currency `504`), the masked PAN (NCAR) and the card expiry (DAEX).
These are threaded automatically by `ProcessPaymentAsync`.

```csharp
// Force legacy v1 (responses terminated with '?')
var legacy = new NapsConfig(host: "192.168.1.100", protocolVersion: ProtocolVersion.V1);
```

Declined results carry a human-readable `PaymentResult.Error` sourced from the
§6 response-code table (e.g. code `116` → `"Insufficient funds — code: 116"`);
approvals are `{000, 001, 003, 007}`.

---

## Payment Flow

The SDK handles the complete two-phase NAPS Pay M2M flow:

```
Your App                    SDK                      Terminal
   │                         │                          │
   │  ProcessPaymentAsync()  │                          │
   │────────────────────────>│                          │
   │                         │   Phase 1 TM 001 ──────>│
   │                         │       (customer taps)    │
   │                         │<────── TM 101 ───────────│
   │                         │   Phase 2 TM 002 ──────>│  ← same connection, ≤ 40 s
   │                         │<────── TM 102 ───────────│
   │<── PaymentResult ───────│                          │
```

> Phase 2 confirmation is sent automatically on the **same TCP connection**, within the 40-second window.

---

## Test Connection

```csharp
bool reachable = await client.TestConnectionAsync();
Console.WriteLine(reachable ? "Terminal reachable" : "Cannot connect");
```

---

## Error Handling

```csharp
try
{
    var result = await client.ProcessPaymentAsync(request);
    // result.IsApproved() / result.Error for soft declines
}
catch (NapsException ex)
{
    switch (ex.Code)
    {
        case NapsErrorCode.ConnectionFailed:
            Console.WriteLine("Cannot reach terminal — check IP and network.");
            break;
        case NapsErrorCode.Timeout:
            Console.WriteLine("Terminal did not respond in time.");
            break;
        case NapsErrorCode.InvalidResponse:
            Console.WriteLine($"Protocol error: {ex.Message}");
            break;
        default:
            Console.WriteLine($"Error [{ex.Code}]: {ex.Message}");
            break;
    }
}
```

Soft declines (card declined, insufficient funds, etc.) are returned as a `PaymentResult` with `IsApproved() == false` — they do **not** throw.
`NapsException` is only thrown for connectivity and protocol-level failures.

---

## Receipts

```csharp
var receipt = result.MerchantReceipt; // or .CustomerReceipt

// Plain text (for display or storage)
Console.WriteLine(receipt.ToPlainText());

// Structured lines (for ESC/POS or custom printer)
foreach (var line in receipt.Lines)
{
    printer.SetBold(line.Bold);
    printer.SetAlignment(line.Alignment switch
    {
        Alignment.Center => PrinterAlignment.Center,
        Alignment.Right  => PrinterAlignment.Right,
        _                => PrinterAlignment.Left
    });
    printer.PrintLine(line.Text);
}
```

---

## PaymentResult Fields

| Property | Type | Description |
|---|---|---|
| `IsApproved()` | `bool` | True when `ResponseCode == "000"` |
| `ResponseCode` | `string` | Raw NAPS Pay response code |
| `Stan` | `string?` | System Trace Audit Number |
| `MaskedCardNumber` | `string?` | PCI-safe masked PAN: `516794******3315` |
| `CardExpiry` | `string?` | YYMM format (e.g. `"3010"`) |
| `GetFormattedExpiry()` | `string` | MM/YY format (e.g. `"10/30"`) |
| `CardholderName` | `string?` | From terminal if available |
| `EntryMode` | `string?` | CC=Contactless, SC=Chip |
| `AuthNumber` | `string?` | Acquirer authorization number |
| `TransactionDate` | `string?` | DDMMYYYY |
| `TransactionTime` | `string?` | HHMMSS |
| `MerchantReceipt` | `Receipt?` | Parsed merchant copy |
| `CustomerReceipt` | `Receipt?` | Parsed customer copy |
| `Error` | `string?` | Human-readable error when not approved |

---

## TLV Protocol Reference

| Tag | Name | Description |
|-----|------|-------------|
| 001 | TM | Message Type |
| 002 | MT | Amount (centimes) |
| 003 | NCAI | Register(2) + Cashier(5) |
| 004 | NS | Sequence Number |
| 007 | NCAR | Masked card number |
| 008 | STAN | System Trace Audit Number |
| 009 | NA | Authorization Number |
| 010 | DP | Receipt data (sub-TLV) |
| 012 | DE | Currency (504 = MAD) |
| 013 | CR | Response Code |
| 017 | DAEX | Expiration Date (YYMM) |
| 018 | DATR | Transaction Date (DDMMYYYY) |
| 019 | HETR | Transaction Time (HHMMSS) |
| 040 | EM | Entry Mode |

---

## Project Structure

```
tkpay-sdk-pos-naps-csharp/
├── src/TKPay.Naps/
│   ├── NapsPayClient.cs          # Main entry point
│   ├── Models/
│   │   ├── NapsConfig.cs         # Connection configuration
│   │   ├── NapsError.cs          # NapsException + NapsErrorCode
│   │   ├── PaymentRequest.cs     # Request data
│   │   ├── PaymentResult.cs      # Result data
│   │   └── Receipt.cs            # Receipt + ReceiptLine + enums
│   ├── Protocol/
│   │   ├── TlvTags.cs            # Tag constants
│   │   ├── TlvProtocol.cs        # TLV builder + parser + PAN masking
│   │   └── ReceiptParser.cs      # DP sub-TLV receipt parser
│   └── Connection/
│       └── NapsConnection.cs     # TCP socket management
└── tests/TKPay.Naps.Tests/
    ├── TlvProtocolTests.cs
    ├── PaymentRequestTests.cs
    └── PaymentResultTests.cs
```

---

## Building & Testing

```bash
cd tkpay-sdk-pos-naps-csharp

# Build
dotnet build

# Test
dotnet test

# Build release package
dotnet pack src/TKPay.Naps/TKPay.Naps.csproj -c Release
```

---

## Requirements

- .NET 6.0+
- Network access to NAPS Pay terminal (same LAN, port 4444)

---

## Security

- PAN (card number) is masked **immediately** when parsed from tag 007 — the full PAN never leaves `TlvProtocol.Parse()`.
- Card numbers in receipt free-text are also masked via regex.
- No card data is logged or stored by the SDK.

---

## License

Copyright 2025 TKpay. All rights reserved.
