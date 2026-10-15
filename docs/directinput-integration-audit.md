# Generic joystick integration audit — 2026-10-15

Scope: the twelve-button output on `feature/directinput-output`, following the
owner's successful r4 preview check. This is a source review with targeted
regression tests; it does not establish normal-installation hardware behaviour.

| Area | Finding and action |
| --- | --- |
| Overview | Joystick choice and selected-output name are present; normal activation remains disabled. Existing shared dropdown styling is retained. |
| Profile Editor | Index 6 maps to the new output without renumbering existing outputs. The preview-only choice and profile options are present. |
| Mapping list and binding dialog | Found a missing label-lookup case: joystick targets could display blank. Added numbered buttons, POV directions, stick targets and trigger-mode labels. Guide/touchpad targets explicitly indicate that the twelve-button device does not send them. |
| Binding diagram | The existing diagram represents canonical mapping targets, not a new Xbox output. Added an explanation visible only for the joystick. Axis roles and trigger behaviour are determined by the profile. |
| Profile persistence | Both XML paths preserve the new identity and custom options. Old profiles get explicit defaults; malformed options prevent activation. |
| Profile transitions and slot reuse | Same-type joystick option changes are checked before reuse. Added tests for layout, trigger mode, threshold, inversion and invalid settings. They require a new compatible output rather than reusing the old configuration. |
| Output Slots | Current/reserved names use the common output display mapping and serialization recognizes the identity. Manual unbound creation deliberately remains unavailable for this preview; use a profile and Dynamic slots. Permanent-slot semantics with custom options are still a release prerequisite. |
| Runtime status | Existing connection/type/fault signals apply to the joystick. Advanced PlayStation haptics are not required for this output. Separate physical audio settings can still affect their own runtime lanes. |
| Old or mismatched backend | Creation/recovery checks require `genericjoystick-v1`, twelve buttons, matching resting axes and accepted native configuration before producer admission. Missing/mismatched responses fail rather than falling back. Added old-response tests and a clear instruction to use the complete matched preview package. |
| Reconnect | Reopening a joystick producer rechecks native readiness and configured resting axes. Existing user-run reconnect/Stop/Start results remain the hardware evidence; no new hardware run was performed for this audit. |
| Built-in HidHide | Generic output is included in the shared VIIPER classification and managed hiding path. Portable lab mode bypasses that integration, so normal-mode hiding still needs explicit validation. |
| Normal packaging | The project still bundles the released VIIPER 0.1.11 backend. The preview builder separately builds and pins the feature backend. Do not enable normal activation with the old bundle or independently replace its binary: the eventual candidate needs matching source, provenance, hashes and installer/portable/update/repair verification. |

The audit fixes affect presentation and an error message. They do not change
the descriptor, input mapping, saved profile schema, backend protocol, package
pins or portable-lab restriction. USB identity request
[pid.codes #1317](https://github.com/pidcodes/pidcodes.github.com/pull/1317) remains
pending. Follow the [release checklist](directinput-release-checklist.md) for
normal-mode work after allocation.

## Automated coverage

`GenericJoystickOutputIntegrationAuditTests` covers readable target names,
preserved mouse/other-output labels, old or mismatched backend responses and
immutable profile compatibility. Existing tests cover mapping frames, profile
serialization, preview gate, Overview state/styles and editor layout. Tests do
not install a driver, start VIIPER or modify HidHide policy.

Local Release/x64 results: all 81 targeted checks passed, then the complete
suite passed with 7,382 tests and twelve existing opt-in skips (7,394 total).
