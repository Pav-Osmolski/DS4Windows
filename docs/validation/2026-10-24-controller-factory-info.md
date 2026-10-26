# Controller factory-information audit — 24 October 2026

## Report and correction

[DS4Windows issue #75](https://github.com/Pav-Osmolski/DS4Windows/issues/75) reported a five-digit year with virtual DS4. VIIPER's feature report `0xA3` places the report ID at byte 0, followed by 16-byte date and time fields. Its builder started the time at byte 16 instead of 17. Host decoding reproduced `Sep 17 20211 1:34:00`; the correct retained metadata is `Sep 17 2021 11:34:00`.

The correction uses date bytes 1–16 and time bytes 17–32 (inclusive), preserving zero padding, the 49-byte report and version fields from byte 33. This does not change the emulated firmware date or physical battery state. Earlier issue comments correctly distinguished firmware metadata from the app build, but missed this DS4 encoding error.

## Other emulated outputs

| Output | Metadata path | Audit result |
| --- | --- | --- |
| DualShock 4 | `0xA3`: two 16-byte NUL-padded strings | Corrected one-byte time offset. Separate `0x81` identity record's 11-byte date field does not concatenate a time. |
| DualSense | `0x20`: contiguous 11-byte date and 8-byte time | Existing offsets 1 and 12 are correct; regression-tested through USB GET_REPORT. |
| DualSense Edge | Same firmware builder as DualSense | Correct offsets; independently constructed and regression-tested. |
| Xbox 360 | USB descriptor/device revision and fixed binary XUSB identity | No Sony-style textual date/time fields or corresponding concatenation. Existing descriptor/report tests retained. |
| Xbox One | Binary GIP Hello, firmware tuple and metadata descriptors | No textual factory timestamp. Existing exact-byte Hello/descriptor/metadata validation retained. |
| Switch 2 Pro | Binary feature responses and factory flash/serial/calibration blocks | No Sony-style factory timestamp. Existing response, identity and flash tests retained. |

This audit concerns factory metadata layout; it does not establish compatibility with every game or host tool. Pending DirectInput work is not part of the stable output set or this release.

## Verification

The new VIIPER `device/factory_info_test.go` decodes actual USB GET_REPORT responses using independent host field boundaries. Twelve cases cover all three Sony personas with defaults and custom leap-day/midnight, 11-hour and 23-hour timestamps. They verify NUL padding, adjacent version fields, report IDs/lengths and truncated host reads. Before the correction, all four DS4 date/time cases failed with the expected concatenated year and missing time digit; DualSense and Edge passed. All twelve cases pass after the correction.

The complete local Go suite passed. Windows/Linux default and Release backend suites, generated-client builds and cross-platform release builds gate the tagged backend. DS4Windows packaging verifies the exact tagged backend, corresponding sources, provenance, licenses and version/hash pins. Main CI tests application packages and installer lifecycle/upgrade preservation before promotion to the stable release.

Implementation: [VIIPER PR #10](https://github.com/Pav-Osmolski/VIIPER/pull/10). Reporter credit: [@Jobima1st](https://github.com/Jobima1st).
