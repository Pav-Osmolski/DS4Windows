# Headset routing and virtual battery preview

Related reports: [#74](https://github.com/Pav-Osmolski/DS4Windows/issues/74) and
[#75](https://github.com/Pav-Osmolski/DS4Windows/issues/75), both from
[@Jobima1st](https://github.com/Jobima1st).

The paired VIIPER source is `bcd7ddecd3c8e011402b3272b4e59a725102ed30`
([draft PR](https://github.com/Pav-Osmolski/VIIPER/pull/8)). Its exact executable,
source archive, hashes, notices and local build provenance ship with the preview.
This records the local testing-only preview before either component was merged
or released stable.

## Software validation

- DS4Windows: 7,783 tests passed, 16 hardware/opt-in tests skipped, no failures.
- VIIPER: complete `go test ./...` suite passed.
- Focused tests cover manual/follow routing, USB/BT DualSense status offsets,
  DS4 headphone flags, old XML compatibility, follow-mode serialization,
  depleted/full/charging states, worker recovery, coalescing, source replacement
  and retirement before stream removal.
- Backend tests inspect actual DS4/DualSense HID battery bytes, strict payload
  rejection, exact-device delivery and preservation of immutable metadata.
- All 48 language/family pairs passed the coverage checks.
- Package composition tests: 33 passed, one opt-in skipped. Eight release
  candidate tests and release-information preservation checks passed.
- Portable ZIP integrity, 24 compiled language satellites, named XAML resources,
  backend identity/hash and testing channel marker were verified.

## Maintainer acceptance

The maintainer reported that the paired preview works well and approved merge
and release on 2026-10-23. The acceptance checklist supplied with that preview
covered audio switching, manual choices, profile persistence and battery bands.
This sign-off is not certification of every supported controller or game;
genuine DS4 and Nintendo hardware coverage remains limited to the actual
hardware available to testers. Synthetic tests cannot certify an audio route
or a particular Windows/game display.

Xbox personas remain wired USB devices. DS4/DualSense browser testing alone
cannot prove Xbox battery support. Factory build-time fields retain valid
emulated firmware dates; they do not describe the DS4Windows build.
