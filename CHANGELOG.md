# Changelog

All notable changes to this project are documented here.

## [1.4.0]

Additive superset release — brings the SDK to feature-parity-or-better with the
standalone `aslan-naps-sdk`. Nothing was removed: `ProcessPaymentAsync`,
`SettlementAsync`, `CancelAsync` and `TestConnectionAsync` are all unchanged.

### Added — management operations on `NapsPayClient`
- **`NetworkTestAsync`** (TM=009) → `NetworkTestResult` `{ Success, ResponseCode, Message }`.
- **`ReferencingAsync(tide = "01", …)`** (TM=013) → `ReferencingResult`
  `{ Success, ResponseCode, ParameterSet, ReceiptText }`. Sends the TIDE (020)
  parameter-set selector: `"01"` merchant (default), `"02"` recharge.
- **`TotalsAsync`** (TM=010) → `TotalsResult` `{ Success, ResponseCode, ReceiptText }`.
- **`DuplicateReceiptAsync`** (TM=008) → `PaymentResult` (reprints the last receipt).
- **`ResetPinPadAsync`** (TM=012) → `Task` (fire-and-forget maintenance command).
- The existing `TestConnectionAsync` (returns `bool`) is kept as-is for back-compat.

### Added — static port helpers
- **`NapsPayClient.FindPort()`** and **`NapsPayClient.ListPorts()`** delegate to
  `PortDiscovery` for one-line serial-port discovery without constructing a client.

### Added — binary TLV (Sunmi) protocol
- **`BinaryTlvProtocol`** (public, `TKPay.Naps.Protocol`) — the binary
  `tag(1) + length(2 BE) + value(N)` framing used by some Sunmi deployments, with a
  2-byte big-endian length prefix. `BuildRequest`, `WrapWithLength`, `ParseResponse`.

### Added — public protocol facade
- **`NapsTlv`** (public, `TKPay.Naps.Protocol`) — a thin facade over the internal
  `TlvProtocol` exposing the ASCII wire format: `BuildField`, `Parse`, `IsApproved`,
  `DescribeResponse`, `MaskPan`, `MaskPansInText`. The internal `TlvProtocol` is
  left internal so existing internal callers are unaffected.

### Added — multi-targeting
- **`net10.0;net9.0;net48`.** The library now also builds for .NET 9 and
  .NET Framework 4.8. net48 compatibility is provided by source polyfills
  (`IsExternalInit`, `Index`/`Range`, `GetValueOrDefault`,
  `string.Contains(StringComparison)`) plus `Microsoft.Bcl.AsyncInterfaces`,
  `System.Memory` and `System.Threading.Tasks.Extensions` for `ValueTask` /
  `IAsyncDisposable` / `Span`. Stream and socket calls are `#if`-guarded to use the
  net48-compatible overloads; there is no functional difference between targets.

## [1.3.0]

### Added — USB serial transport (Ingenico Lane/3000)
- **Transport switch.** `NapsConfig` now carries a `TransportType` (`TcpSocket`
  default / `UsbSerial`). The existing `NapsConfig(host, …)` constructor is TCP;
  the new `NapsConfig.Serial(portName, baudRate, …)` factory builds a USB serial
  (CDC-ACM) config. `NapsPayClient` selects the transport automatically — the
  public payment/settlement/cancel/test API is unchanged for both.
- **Serial config.** `SerialPortName` (nullable → auto-discover) and
  `SerialBaudRate` (default **115200**, `NapsConfig.DefaultBaudRate`).
- **Port auto-discovery.** `PortDiscovery.FindIngenicoPort()` locates the Ingenico
  port by USB vendor id **0x0B00** (2816) with a CDC-ACM fallback (`usbmodem` /
  `ttyACM`), cross-platform (Windows registry, macOS `ioreg`, Linux sysfs).
  `PortDiscovery.ListPorts()` lists all serial ports and
  `PortDiscovery.SelectIngenicoPort(candidates)` exposes the pure selection logic.
- **Per-transport protocol default.** When `protocolVersion` is not set explicitly,
  it resolves from the transport: serial → **v1** (`'?'`), TCP → **v2** (`'!'`).
  An explicit `protocolVersion` argument still wins.
- **Serial framing.** The USB serial link uses the same ASCII TLV framing as TCP:
  the raw TLV request is written with no terminator, and the response is read until
  the resolved terminator or a ~1-second inter-chunk silence fallback.

### Changed
- `NapsConfig.ProtocolVersion` now resolves per transport instead of always
  defaulting to V2; the effective value for existing TCP callers is unchanged (V2).
- Added a `System.IO.Ports` dependency for the serial transport.

## [1.2.1]

### Added — NAPS TLV v2 protocol
- **Enriched Payment Confirmation (TM 002).** `TlvProtocol.BuildConfirmationRequest`
  now optionally carries the amount (MT 002) + currency (DE 012), the masked PAN
  (NCAR 007) and the card expiry (DAEX 017), echoed from the approved Payment
  Response (TM 101) so the terminal can cross-check the frame it is confirming.
  Field order: `TM, NCAI, NS, [MT, DE], STAN, [NCAR], [DAEX], DA, HE`. The extra
  arguments are optional — existing three-argument callers are unchanged.
- **Protocol version option.** `NapsConfig` accepts a `ProtocolVersion` (`V1`/`V2`),
  defaulting to `V2`. It selects the expected response terminator: `'!'` for v2,
  `'?'` for v1 (exposed as `NapsConfig.Terminator`).
- **Response-code table (§6).** An internal CR (013) lookup maps codes to
  human-readable messages (`TlvProtocol.DescribeResponse` / `ResponseCodes`) and
  classifies the approval set `{000, 001, 003, 007}` (`TlvProtocol.IsApproved`).
  Declined `PaymentResult.Error` now sources its message from this table.

### Changed
- The payment flow threads MT/NCAR/DAEX from Phase 1 into the Phase-2 confirmation.
- The TLV parser and socket read loop stop at either terminator (`'!'` or `'?'`),
  making them protocol-version agnostic.
