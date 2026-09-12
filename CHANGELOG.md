# Changelog

All notable changes to this project are documented here.

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
