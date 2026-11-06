# TKPay NAPS POS SDK — C#

> **Canonical NAPS SDK.** This is the reference implementation of the NAPS Pay M2M TLV
> integration; the other language SDKs track it. Part of a family that all speak the same
> protocol to the same terminals:
> **C# (this repo, canonical)** ·
> [Node.js](https://github.com/Belkouche/aslan-naps-node) ·
> [Android](https://github.com/Belkouche/tkpay-sdk-pos-android) ·
> [iOS](https://github.com/Belkouche/tkpay-sdk-pos-ios) ·
> [React Native](https://github.com/Belkouche/tkpay-sdk-pos-rn).
> Test without hardware using the [emulator](https://github.com/Belkouche/tkpay-naps-emulator).
> (Supersedes the retired `aslan-naps-sdk`.)

C# SDK for integrating NAPS Pay terminals via the M2M TLV protocol, over TCP
(Sunmi P2/P3, port 4444) or USB serial (Ingenico Lane/3000, CDC-ACM). Targets
`net10.0`, `net9.0` and `net48` (.NET Framework 4.8).

- Multi-targeted: **`net10.0` / `net9.0` / `net48`** (.NET Framework 4.8)
- Two transports: TCP socket (default) and USB serial with port auto-discovery
- Full payment API plus management operations (network test, referencing, totals,
  duplicate receipt, reset PIN pad)
- Binary TLV (Sunmi) protocol and a public ASCII wire-format facade
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
    protocolVersion:      ProtocolVersion.V2         // Default per transport (TCP → V2)
);
```

---

## Transports (TCP / USB serial)

The SDK reaches the terminal over one of two transports, selected by which config
you build:

- **TCP socket** (default) — Sunmi P2/P3 terminals. Use the `NapsConfig(host, …)`
  constructor. Defaults to protocol **v2** (`'!'` terminator).
- **USB serial** — Ingenico Lane/3000 over USB CDC-ACM. Use `NapsConfig.Serial(…)`.
  Defaults to protocol **v1** (`'?'` terminator).

```csharp
// Sunmi over TCP (unchanged)
var tcp = new NapsConfig(host: "192.168.1.100");

// Ingenico Lane/3000 over USB serial — auto-discover the port
var serial = NapsConfig.Serial();                       // portName: null → auto-discover
var pinned = NapsConfig.Serial("/dev/cu.usbmodem1401"); // or pin an explicit port
var custom = NapsConfig.Serial(baudRate: 115200);       // 115200 is the default

// Same client API for both transports
var client = new NapsPayClient(serial);
var result = await client.ProcessPaymentAsync(
    new PaymentRequest(amount: 150.00m, registerId: "01", cashierId: "00001"));
```

The USB serial link uses the **same ASCII TLV framing** as TCP: the raw TLV request
is written with no terminator, and the response is read until the resolved terminator
(`'!'`/`'?'`) or a ~1-second inter-chunk silence.

### Port auto-discovery

When `NapsConfig.Serial()` is given a `null` port name, the SDK auto-discovers the
Ingenico port by USB vendor id **0x0B00** (2816), falling back to a CDC-ACM device
(`usbmodem` / `ttyACM`) if no VID match is found. Discovery is cross-platform
(Windows registry, macOS `ioreg`, Linux sysfs). Helpers:

```csharp
string[] all   = PortDiscovery.ListPorts();          // all serial ports
string?  ingen = PortDiscovery.FindIngenicoPort();    // best Ingenico match, or null
```

---

## Protocol version (v1 / v2)

The protocol version defaults **per transport** when not set explicitly: TCP/Sunmi →
**v2** (`'!'`), USB serial/Ingenico → **v1** (`'?'`). An explicit `protocolVersion`
argument always wins. The version selects the end-of-message terminator expected on
the wire, though the parser and reader tolerate either terminator regardless of the
configured version.

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
> If the terminal refuses the confirmation (TM 102 with CR ≠ 000, e.g. 482 — the terminal
> auto-reverses), the result is **not approved**.

## Cancellation (void) Flow

A void is two-phase on one connection (spec v2.0 §4.3/§4.4, NapsPay v5.4.4):

```csharp
var v = await client.CancelAsync(stan: p.Stan!, registerId: "01", cashierId: "00001",
                                 sequence: "000001", amountCentimes: 15000);
// TM 003 → TM 103 (transaction found, merchant receipt)   — nothing reversed yet
// TM 004 → TM 104 (reversal executed, void STAN, customer receipt)
switch (v.Status)
{
    case CancellationStatus.Completed: /* TM 104 CR 000 or 480 */ break;
    case CancellationStatus.Failed:    /* refused — nothing reversed */ break;
    case CancellationStatus.Pending:   /* unknown: no TM 104 reply, or CR 995 */
        // Verify before retrying: reprint (TM 008) and check the receipt for v.Stan.
        break;
}
```

`Pending` is fail-closed: the reversal may or may not have executed, so it is never
reported as `IsSuccess`. It keeps `Stan` (the transaction being cancelled) and the
TM 103 `MerchantReceipt`. TM 004 carries the sequence in NS (same value as TM 003) and
echoes the amount (MT) returned in TM 103 — the v5.4.4 terminal refuses TM 004 without it.

---

## Test Connection

```csharp
bool reachable = await client.TestConnectionAsync();
Console.WriteLine(reachable ? "Terminal reachable" : "Cannot connect");
```

---

## Management Operations

Beyond payments, the client exposes the NAPS Pay management operations. Each opens
and closes its own connection and returns a typed result.

```csharp
// Network test / ping (TM=009)
var net = await client.NetworkTestAsync();
Console.WriteLine(net.Success ? "Network OK" : $"Failed [{net.ResponseCode}]: {net.Message}");

// Referencing — load a parameter set (TM=013). TIDE "01" = merchant, "02" = recharge.
var refr = await client.ReferencingAsync(tide: "01");
Console.WriteLine($"Referencing [{refr.ResponseCode}] set={refr.ParameterSet}");

// Totals report (TM=010)
var totals = await client.TotalsAsync();
Console.WriteLine(totals.ReceiptText);

// Reprint last receipt (TM=008) — returns a PaymentResult
var dup = await client.DuplicateReceiptAsync();
Console.WriteLine(dup.MerchantReceipt?.ToPlainText());

// Reset the PIN pad (TM=012) — fire-and-forget
await client.ResetPinPadAsync();
```

| Method | TM | Returns |
|---|---|---|
| `NetworkTestAsync` | 009 | `NetworkTestResult { Success, ResponseCode, Message }` |
| `ReferencingAsync(tide = "01")` | 013 | `ReferencingResult { Success, ResponseCode, ParameterSet, ReceiptText }` |
| `TotalsAsync` | 010 | `TotalsResult { Success, ResponseCode, ReceiptText }` |
| `DuplicateReceiptAsync` | 008 | `PaymentResult` |
| `ResetPinPadAsync` | 012 | `Task` |

Static port helpers are also available without constructing a client:

```csharp
string[] all  = NapsPayClient.ListPorts();  // all serial ports
string?  port = NapsPayClient.FindPort();    // best Ingenico match, or null
```

---

## Protocol access (ASCII + binary TLV)

The internal builder/parser (`TlvProtocol`) stays internal, but the wire format is
available through two public types in `TKPay.Naps.Protocol`:

- **`NapsTlv`** — ASCII TLV facade: `BuildField`, `Parse`, `IsApproved`,
  `DescribeResponse`, `MaskPan`, `MaskPansInText`.
- **`BinaryTlvProtocol`** — binary framing for some Sunmi deployments:
  `tag(1) + length(2 BE) + value(N)` with a 2-byte big-endian length prefix.
  `BuildRequest`, `WrapWithLength`, `ParseResponse`.

```csharp
using TKPay.Naps.Protocol;

// ASCII TLV
var frame  = NapsTlv.BuildField("001", "001");
var fields = NapsTlv.Parse(response);
bool ok    = NapsTlv.IsApproved(fields.GetValueOrDefault("013"));

// Binary TLV (Sunmi)
var body    = BinaryTlvProtocol.BuildRequest(BinaryTlvProtocol.TxnTypeSale, "15000", "ORDER-1");
var wire     = BinaryTlvProtocol.WrapWithLength(body);
var response = BinaryTlvProtocol.ParseResponse(bodyBytes);
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
├── src/TKPay.Naps/                       # net10.0;net9.0;net48
│   ├── NapsPayClient.cs          # Main entry point (payments + management ops)
│   ├── Models/
│   │   ├── NapsConfig.cs         # Connection configuration
│   │   ├── NapsError.cs          # NapsException + NapsErrorCode
│   │   ├── PaymentRequest.cs     # Request data
│   │   ├── PaymentResult.cs      # Result data
│   │   ├── SettlementResult.cs / CancellationResult.cs
│   │   ├── NetworkTestResult.cs / ReferencingResult.cs / TotalsResult.cs
│   │   └── Receipt.cs            # Receipt + ReceiptLine + enums
│   ├── Protocol/
│   │   ├── TlvTags.cs            # Tag + message-type constants
│   │   ├── TlvProtocol.cs        # ASCII TLV builder + parser + PAN masking (internal)
│   │   ├── NapsTlv.cs            # Public ASCII wire-format facade
│   │   ├── BinaryTlvProtocol.cs  # Public binary (Sunmi) TLV protocol
│   │   └── ReceiptParser.cs      # DP sub-TLV receipt parser
│   ├── Polyfills/                # net48 source shims (IsExternalInit, Index/Range, compat)
│   └── Connection/
│       ├── NapsConnection.cs     # TCP socket management
│       ├── SerialConnection.cs   # USB serial (CDC-ACM)
│       └── PortDiscovery.cs      # Cross-platform serial port discovery
└── tests/TKPay.Naps.Tests/
    ├── TlvProtocolTests.cs
    ├── ManagementRequestTests.cs
    ├── BinaryTlvProtocolTests.cs
    ├── NapsTlvFacadeTests.cs
    ├── PaymentRequestTests.cs
    ├── PaymentResultTests.cs
    └── SerialTransportTests.cs
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

- .NET 9.0+ or .NET 10.0 (also builds for .NET Framework 4.8 via `net48`)
- Network access to NAPS Pay terminal (same LAN, port 4444)

---

## Security

- PAN (card number) is masked **immediately** when parsed from tag 007 — the full PAN never leaves `TlvProtocol.Parse()`.
- Card numbers in receipt free-text are also masked via regex.
- No card data is logged or stored by the SDK.

---

## License

Copyright 2025 TKpay. All rights reserved.
