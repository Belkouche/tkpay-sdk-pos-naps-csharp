# Changelog

All notable changes to this project are documented here.

## [Unreleased] — Ingenico Lane3600

Ported from the Node SDK after live testing on a Lane3600 over USB.

- **USB serial: payment confirmation no longer lost.** The Lane3600 holds back every
  answer until about 30 s after the port opens. When a payment was the first frame,
  its TM 101 arrived 30 s late, after the terminal had stopped waiting for TM 002: it
  showed "attente validation caisse", timed out, and the authorised payment was not
  recorded. The first frame now waits until the port has been open 32 s. Keep the
  connection open between transactions so the wait happens once.
- **USB serial: one port for the life of the client.** `NapsPayClient` now keeps the
  serial port open between operations instead of reopening it for each one, which
  would have cost the Lane3600's ~30 s wait every time. `NapsPayClient` is now
  `IAsyncDisposable`; dispose it to close the port. TCP still opens one connection per
  operation. Run `NetworkTestAsync()` when the point-of-sale app starts so the first
  sale does not wait.
- **USB serial: back-to-back operations.** After a reply the Lane3600 keeps showing or
  printing the result for a moment, and a request arriving then was lost: a void sent
  right after a payment got no reply and timed out, and a payment sent 0.5 s after a
  network test was refused (CR 100) without asking for a card. A request that starts a
  new operation now waits 3 s after the terminal's last reply; the confirmations
  (TM 002, TM 004) wait 0.5 s. Requests also end with the protocol terminator ('!' v2,
  '?' v1), as in the Node SDK. Verified on the terminal: network test → 1 MAD payment →
  immediate void, all completed (sale 000256, void 000257).
- **Reader (USB and TCP): end of reply on a field boundary.** A reply is complete when
  walking its fields by length lands on a '!'/'?' terminator. It used to end whenever a read
  happened to stop on one, and receipt lines inside DP end with '?', so an unlucky ~1 KB read
  could cut a reply short. Non-TLV data (the Lane3600 writes 999 as the length of a longer
  receipt) still ends on a trailing terminator.
- **USB serial: port closed after a failed exchange.** After a timeout or error the
  kept-open port is closed, so a reply that arrives late is never read as the answer
  to the next request. The next operation reopens it (and waits again).
- **Network test is USB only.** Over TCP, `NetworkTestAsync` sends nothing and returns
  `ResponseCode = "UNSUPPORTED"`. The NapsPay app on a Sunmi P2 answers TM 009 with
  TM 101 / CR 902 (it only handles payments and voids over the ECR link), and a test
  frame crashed the app.
- **Void frames follow spec v2 §4.3/§4.4.** TM 003 now carries the mandatory NS (a new
  sequence for the void) and sends the payment's `sequence` in NSA. Without NS the
  Lane3600 refused it with CR 307 (format error). TM 004 repeats the TM 003 NS and
  echoes MT, DE, DATR and HETR from TM 103, in the spec order: TM, NCAI, NS, STAN, MT,
  DE, DATR, HETR, DA, HE. The frames match the Node SDK frames that completed a void
  on a live Lane3600 (TM 104 CR 000).
- **Void receipt.** When TM 103 carries no receipt, as on the Lane3600, `CancelAsync`
  returns the TM 104 receipt (the only void receipt, "SIGNATURE CLIENT") as
  `MerchantReceipt` instead of `CustomerReceipt`. Terminals that send a receipt with
  TM 103 are unchanged.

## [1.5.1]

- **Card numbers masked everywhere (first 6 + last 4 digits).** Tag 007 is always
  returned as first 6 + `*` per hidden digit + last 4, whatever its grouping. In
  receipt text, every run of 13+ digits (adjacent, or separated by exactly one space,
  dash, dot, tab or non-breaking space; no upper length limit) is masked except its
  first 6 and last 4 digits — it previously caught only 16 contiguous digits. Dates,
  times, amounts and shorter numbers are untouched. The raw DP block is masked at
  parse time (every sub-tag value of each line), so `TotalsResult.ReceiptText`, `ReferencingResult.ReceiptText` and
  `NapsTlv.Parse` never carry a full number. Masking keeps the text length, so the
  rest of each receipt line and the DP structure are unchanged. The full number is
  never exposed or logged; the SDK writes no logs.

## [1.5.0]

NapsPay v5.4.4 sync — gaps found by cross-checking the NAPS Pay ECR message
definitions, `aslan-naps-sdk` and the emulator, then corrected against the official
NapsPay ECR TLV Protocol Specification v2.0 (§4.3, §4.4, §6) and the live v5.4.4
terminal test recorded in `tkpay-sdk-pos-naps-rn` 5fcfeb0.

### Added
- **Two-phase void.** `CancelAsync` now completes the cancellation flow on the same
  connection: TM 003 → TM 103 (CR 000 = *transaction found*, nothing reversed yet),
  then **TM 004 (Cancellation Confirmation)** → TM 104, which executes the reversal.
  Previously only TM 003 was sent.
- **`CancellationStatus` (`Completed` / `Failed` / `Pending`)** and
  `CancellationResult.Status` / `IsPending`. TM 104 classification:
  - CR **000 or 480** → `Completed` (spec: "CR = 480 (or 000): cancellation completed").
  - CR **995** → `Pending` — the spec calls it "Server processing error", but it was
    seen with successful voids on the live terminal.
  - **No TM 104 reply** (timeout after TM 004) → `Pending` — fail closed, never a
    successful void.
  - Anything else → `Failed`.

  A pending result keeps `Stan` (the transaction being cancelled) and the TM 103
  `MerchantReceipt` so the app can verify via duplicate receipt (TM 008).
- **TM 004 frame** (`TlvProtocol.BuildCancellationConfirmationRequest`):
  `TM, MT, STAN, NCAI, NS, DA, HE`. The sequence goes in **NS (004)**, the same
  value as the TM 003 it confirms, not NSA (005). MT is echoed verbatim from TM 103
  (falling back to the request amount); the live terminal refuses TM 004 without it
  (CR 909).
- **Parsed receipts on report results:** `TotalsResult.Receipt`,
  `ReferencingResult.Receipt` and `SettlementResult.Receipt` (TM 110 / TM 113 DP).
  `ReceiptText` keeps the raw block.
- **Receipt tests:** golden merchant/customer copies parsed from captured emulator
  DP blocks (payment and void), and an `approve`-mode emulator integration test for
  both copies of a payment and its void.
- **`DuplicateReceiptByStanAsync(stan, …)`** — TM 008 carrying the STAN (spec §4.6)
  to reprint a given transaction of the current business day, e.g. to check a
  pending void. `DuplicateReceiptAsync` (last transaction) is unchanged.
- `CancellationResult.MerchantReceipt` (DP of TM 103) and
  `CancellationResult.CustomerReceipt` (DP of TM 104). On completion `Stan` is the
  void STAN from TM 104.
- **`examples/Quickstart`** — runnable minimal payment example (same code as the
  README quickstart), part of the solution build. README rewritten as an
  integrator guide.
- Full NapsPay ISO response-code table in `TlvProtocol.ResponseCodes` /
  `NapsTlv.DescribeResponse` (104, 107–129, 180–184, 200–210, 265, 281, 299–309,
  381–385, 400, 481, 500, 503, 581, 582, 800, 880, 888, 902, 908, 992–994).

- **Emulator void integration tests** (`EmulatorVoidIntegrationTests`, trait
  `Category=Integration`): one test per emulator mode (`approve`, `void_480`,
  `void_995`, `void_no_confirm`, `void_refuse --code 302`), skipped unless
  `NAPS_EMULATOR_MODE` matches. Port via `NAPS_EMULATOR_PORT` (default 4444).

### Fixed
- **Receipts now reproduce the terminal's DP block exactly (spec §5).** The parser
  split the block on `*`, which cut the masked-PAN line (`516794******3315`) and left
  it blank; it now reads lines by TLV length, treating `*` / `?` as separators only at
  a field boundary. Every line is kept in order with its line number, bold and
  alignment; nothing is added, merged or blanked.
- **No SDK-added receipt text.** Removed the "Powered by NAPS" footer and the
  rewrite of centred "Naps" lines to "TKPAY".
- **'!' / '?' inside a frame no longer truncate it.** `TlvProtocol.Parse` stops at a
  terminator only on a field boundary, and the transports no longer strip from the
  last '!' / '?' (TM 103/104 arrive without a terminator, so receipt text containing
  either character used to cut the frame).
- **Refused payment confirmation reported as approved.** A TM 102 with CR ≠ 000
  (e.g. 302 sale not found, 482 already cancelled — the terminal auto-reverses) now
  yields a failed `PaymentResult` instead of an approval.

### Changed
- `CancellationResult.IsSuccess` is now computed from `Status` (no longer
  `init`-settable) and is true only for `Completed`.
- A terminal that answers TM 003 directly with TM 104 is classified with the same
  TM 104 rules (previously only CR 000 was accepted).
- `TlvTags.MsgCancellationCorrection` (internal) renamed to
  `MsgCancellationConfirmationResponse` ("104") and
  `MsgCancellationConfirmationRequest` ("004") added — 104 is the reply to TM 004,
  not a "correction".

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
