# Generic joystick release preparation

## Current design

The current feature uses a fixed sixteen-button device with per-profile numbered
assignments. See [the current design and migration rules](directinput-development.md#fixed-sixteen-button-design-2026-10-26).
Earlier dated evidence retains its original scope; native/game acceptance of the
sixteen-button candidate is still required before release.

Status: **registered sixteen-button output integrated into standard local packages**.
Both builds use the coordinated VIIPER pin and normal startup flow. Installed
HidHide and game acceptance still need hardware testing.

See the [integration audit](directinput-integration-audit.md) for source-review
findings and the [setup guide](directinput-preview-guide.md) for configuring the output.
The [installed acceptance checklist](directinput-installed-acceptance.md) covers
remaining normal setup, upgrade, built-in HidHide and saved-game binding checks.
The [final preview review](directinput-final-review.md) records the reviewed
branch revisions, CI evidence, package-validator correction and release gates.

## Evidence already collected

- Native synthetic input: all 156 observations and 312 x86/x64 DirectInput/WinMM
  comparisons passed across two layouts and four trigger modes.
- USB DualSense: the owner confirmed the complete layout/trigger matrix, profile
  persistence, inversion/threshold controls, unplug/reconnect and Stop/Start.
- BeamNG.drive 0.39.4.0.20972: Racing + Full range, X steering, Rz throttle,
  Z brake; bindings and driving passed.
- FlatOut 2, current Steam installation: Legacy + Centred, X steering, Ry throttle,
  Rx brake; driving and both manual/automatic transmission passed. Exact game
  build was not recorded. Stick menu navigation was awkward at 0% game dead zone.
- Saved-binding follow-up on r8: after the restart/reconnect checks requested for
  these games, the owner reported all seemed fine and supplied reports
  `20261015-182751-e71f6be9` and `20261015-184143-bc25c694`. Both had clean launcher
  cleanup and no backend warnings/errors. Binding persistence is owner-reported;
  logs do not capture game bindings, and built-in HidHide remained bypassed.
- The corrected r4 preview retained main's Overview dropdown styling and normal
  `Lang` packaging. The owner confirmed the fix. Both CI runs at DS4Windows
  `3e450abfab68d929a806e32a6060c3e7eda01886` passed.

See [development evidence](directinput-development.md) for reports, revisions,
automated checks and the distinction between logs and user observations.

## 1. Obtain and adopt an identity

- [x] Submit the prepared pid.codes request with the maintainer's authorization:
  [pid.codes PR #1317](https://github.com/pidcodes/pidcodes.github.com/pull/1317),
  submitted on 2026-10-15.
- [x] Confirm acceptance and adopt [1209:D10C](https://pid.codes/1209/D10C/) in
  the matched sixteen-button preview. Allocation was approved on 2026-11-25.
  Existing `FFFF:D10C`/`FFFF:D110` remain lab IDs.
- [ ] Freeze the approved sixteen-button descriptor and validate its native
  enumeration and game bindings before enabling normal activation.
- [ ] Freeze the production descriptor, strings and versioning policy; document
  that old preview game bindings may need to be recreated for the new identity.
- [ ] Repeat native enumeration, resting-axis checks and binding persistence
  using the actual production identity. Test two simultaneous controllers and
  reconnect ordering before claiming stable multi-controller bindings.

## 2. Integrate normal operation

The Profile Editor and Overview both enable Generic joystick in standard builds.
Backend creation verifies the versioned contract, registered USB identity,
sixteen buttons, requested resting axes and accepted native USB configuration.

- [x] Use the normal bundled-backend verification and reject a missing or
  mismatched Generic joystick contract, including a lab USB identity.
- [x] Enable the choice consistently in Profile Editor and Overview, retaining
  shared styles and existing output numeric identities.
- [ ] Verify Overview, Controllers, Output Slots and diagnostics describe the
  actual joystick output. Output Slots already uses the common display-name
  mapping; retain Dynamic slots until reservation semantics for profile-specific
  immutable joystick options have been explicitly validated.
- [ ] Test changing layout/trigger options with a live controller, switching to
  every existing output, malformed/old profiles and service restart.

### Built-in HidHide

`ViiperOutDevice.IsViiperType` already includes the generic joystick, so normal
`ControlService.EnsureHidHideForVirtualOutput` uses the existing managed physical
device hiding path. A new external hiding workflow is not required.

The preview's `PortableLabContext` deliberately bypasses this system integration;
the existing game reports do **not** validate automatic HidHide. After normal
activation is available, check:

- [ ] With built-in hiding enabled, games see the virtual joystick while the
  DS4Windows allowlisted process still reads the physical controller.
- [ ] Unplug/reconnect, Stop/Start, profile switching and app exit restore only
  policy owned by the app, preserving the user's existing HidHide configuration.
- [ ] Without HidHide installed, or with hiding disabled, input still works and
  guidance explains the possible duplicate physical device.
- [ ] Record Steam Input and HidHide state with each subsequent game result.

## 3. Build a matched release candidate

- [ ] Package the pinned backend through the normal installer and portable paths;
  preserve hash/ownership verification, localization and update/repair behaviour.
- [ ] Run both repositories' required CI and relevant profile, output lifetime,
  HidHide and packaging regression checks on the candidate revisions.
- [ ] Test a clean install, upgrade, repair and portable extraction, without
  changing an existing user's output selection or enabling extra device support.
- [ ] Publish setup guidance, axis-layout tables and tested game configurations.
  State input-only support, no force feedback/audio, sixteen buttons and limits of
  evidence. NFS Underground 2, Serious Sam and Jedi Knight remain unverified.
- [ ] Supply the matched candidate for owner testing before merge/release approval.

This checklist does not authorize merging, publishing a release, installing a
driver or submitting an external identity request.

## Multi-controller identity acceptance

The [identity investigation](directinput-identity-audit.md) records the completed
synthetic test and read-only Windows/transport research. Input isolation passed,
but removing A reassigns B's DirectInput GUID along with its joystick slot.
Distinct USB serials and HID paths persisted. This is still an identity-test
failure; no registry or transport workaround has been applied.

- [ ] Resolve the GUID persistence failure with a supported, validated change,
  or explicitly approve a documented compatibility scope before normal release.
- [ ] Test saved-game bindings for that chosen scope; synthetic inputs alone do
  not establish how each game handles a reassigned identifier.

The existing production-identity, normal-packaging and HidHide checks remain
open. Repeating the same diagnostic package is not a substitute for these items.


## Upstream USB/IP serial-completion retest

- [ ] When a packaged USB/IP release containing
  [a2a1159](https://github.com/vadimgrn/usbip-win2/commit/a2a1159763088c85d940078cfa6491f38540953d)
  is available, verify inclusion and perform the controlled comparison described
  in the [identity audit](directinput-identity-audit.md#pending-usbip-serial-fix-controlled-retest-required).
  Later successful serial reads do not establish correct initial PnP handling.
- [ ] Keep the known GUID reassignment and saved-binding limits visible until
  new evidence establishes the result.

On 2026-11-25, pid.codes merged PR #1317. The standard local build uses that
registered identity with the pinned USB/IP 0.9.8.1 driver. Multi-controller GUID
persistence with the registered identity and installed HidHide still need
native Windows acceptance testing.

## Standard package preparation

See [normal packaging preparation](directinput-normal-packaging.md) for the
implemented backend-pin, preview-isolation and upgrade-ownership checks, and the
coordinated promotion steps. The 5.0.84 local builds use VIIPER 0.1.19 with
normal Generic joystick activation. The older packaging rehearsal used VIIPER
0.1.11. Automated HidHide policy and lifecycle checks leave the
normal-installation hardware test open.
