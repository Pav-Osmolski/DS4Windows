# Controller info preview validation

- Production WPF window rendered under default/light and dark resources with
  separate physical and emulated snapshots. Values remain selectable.
- Real controller card template measured for matching left/right inset under
  both base themes; other list defaults retained.
- Synthetic Sony reports exercise correct report IDs, date/time boundaries,
  hardware/firmware values, malformed/truncated data and unknown battery states.
- Xbox public versions and Nintendo battery bands/voltage are checked without
  manufacturing unsupported Sony fields.
- Backend tests query actual registered DS4, DualSense, Edge, Xbox 360, Xbox One
  and Switch 2 Pro devices, reject invalid requests, distinguish identical model
  instances, and check that queries preserve metadata and device ownership.
- Backend default and release-tag suites pass. These synthetic tests do not
  establish firmware availability on every physical controller model.
- New strings are present in all 24 supported language packs; structural coverage
  and UTF-8 checks pass. Native speaker review remains welcome.

This is a local testing preview, pending maintainer hardware/UI review.

Local results: application suite 7,793 passed / 16 opt-in hardware tests skipped;
standalone verification suite 75 passed; final information/theme tests 9 passed.
Packaging tests: 34 passed with one platform-specific skip; release-candidate
contracts: 8 passed. All 48 language/family pairs pass validation. The portable
ZIP passed integrity, exact managed-manifest, locale and paired-backend hash checks.

Layout revision 2: custom rounded tabs replace the native tab header, with no
bottom rule. Render/contrast assertions cover both selected and unselected
labels under Light, Dark, Graphite, Paper, Midnight and High contrast. Single-line
values are limited to compact content height; 9 focused tests pass.

Preview revision 3 excludes descriptor index zero from manufacturer/product/serial
identity strings: index zero contains USB language IDs, not a serial number.
Default and release-tag API tests cover absent and present strings across actual
registered controller types. Frontend tests check optional metadata serial
fallback. The info API bypasses Windows USB/IP string descriptor completion, so
this display correction is independent of usbip-win2 commit a2a1159.

Preview revision 4 removes the solid accent border caused by automatic or
restored focus on Controller info buttons. Native keyboard-only focus visuals
remain enabled, together with hover/pressed states. The 10 focused tests pass.
