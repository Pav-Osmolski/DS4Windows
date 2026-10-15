# Generic joystick release preparation

Status: **working twelve-button preview; not ready for a normal release**.
The paired `feature/directinput-output` branches remain the development path.
Do not remove the portable-lab gate or replace the released backend independently.

See the [integration audit](directinput-integration-audit.md) for source-review
findings and the [preview guide](directinput-preview-guide.md) for tester setup.

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
- The corrected r4 preview retained main's Overview dropdown styling and normal
  `Lang` packaging. The owner confirmed the fix. Both CI runs at DS4Windows
  `3e450abfab68d929a806e32a6060c3e7eda01886` passed.

See [development evidence](directinput-development.md) for reports, revisions,
automated checks and the distinction between logs and user observations.

## 1. Obtain and adopt an identity

- [x] Submit the prepared pid.codes request with the maintainer's authorization:
  [pid.codes PR #1317](https://github.com/pidcodes/pidcodes.github.com/pull/1317),
  submitted on 2026-10-15. Allocation remains pending.
- [ ] Confirm acceptance and record the registry entry/request URL. The proposed
  `1209:D10C` is **not assigned**. Existing `FFFF:D10C`/`FFFF:D110` remain lab IDs.
- [ ] Change only the approved twelve-button descriptor. Leave sixteen buttons
  lab-only; it needs separate validation and a different approved identity.
- [ ] Freeze the production descriptor, strings and versioning policy; document
  that old preview game bindings may need to be recreated for the new identity.
- [ ] Repeat native enumeration, resting-axis checks and binding persistence
  using the actual production identity. Test two simultaneous controllers and
  reconnect ordering before claiming stable multi-controller bindings.

## 2. Integrate normal operation

The allocation, saved-profile and mapped-input paths already support the preview.
The Profile Editor and Overview show it, but deliberately prevent normal-mode
activation. Backend creation verifies the versioned joystick contract, twelve
buttons, requested resting axes and accepted native USB configuration.

- [ ] Admit normal activation only with the approved identity and the matched,
  verified backend package. Confirm a stale backend fails clearly without
  leaving imports or silently falling back to another output.
- [ ] Enable the choice consistently in Profile Editor and Overview after that
  prerequisite, retaining shared styles and existing output numeric identities.
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
  State input-only support, no force feedback/audio, twelve buttons and limits of
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
  new evidence establishes the result. Continue other preview preparation.

Checked on 2026-10-15: pid.codes PR #1317 remains open with no review; USB/IP's
latest published release remains v.0.9.8.1. Both feature branches contain their
current main branches. These checks do not approve normal activation or release.

## Standard package preparation

See [normal packaging preparation](directinput-normal-packaging.md) for the
implemented backend-pin, preview-isolation and upgrade-ownership checks, and the
coordinated promotion steps. Standard-format packaging proofs retain released
VIIPER 0.1.11 and the generic-output activation gate; they are not matched
feature release candidates. Automated HidHide policy/lifecycle checks do not
replace the outstanding normal-installation hardware test.
