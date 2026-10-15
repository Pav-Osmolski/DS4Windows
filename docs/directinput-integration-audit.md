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

## r5 readiness popup follow-up

Report `20261015-154154-8933a7c0` and its screenshot exposed a separate readiness
probe failure when entering Button Mapping: `Connect: ObjectDisposedException`.
The backend stream ran from 15:42:07 to 15:43:04 despite the failed check; the
report recorded no launcher failure, cleanup errors or remaining imports. A
handshake EOF was logged at 15:42:13. These facts support a failed probe rather
than establishing that the active joystick stream stopped.

The readiness probe still used `BeginConnect` and disposed its APM completion
wait handle. The main API client already uses a cancellable `ConnectAsync` path
to avoid that completion-handle lifetime hazard. The readiness probe now uses
that pattern too, retaining its 750 ms connect budget, whole-probe deadline,
authenticated encrypted ping and key-mismatch rejection. It does not bypass
readiness, retry with plaintext or change backend ownership.

Follow-up tests cover 256 consecutive loopback readiness checks, sixteen
authenticated lab pings, wrong-key rejection and the existing trickled-response
deadline tests. All 91 targeted checks passed. The simple repeated-check fixture
also passed before the fix; it exercises the path but does not deterministically
reproduce the user's timing-dependent exception. Opening Controls in the next
matched preview remains the required owner confirmation.

The complete follow-up Release/x64 suite passed: 7,383 passed and twelve existing
opt-in skips (7,395 total).

## r6 owner confirmation and action-window refinement

The owner confirmed that the readiness popup no longer occurred, mapping labels
and unavailable-button explanations displayed correctly, switching output to
Xbox 360/DualShock 4 worked, and saved profile settings survived reopening.
Report `20261015-155130-2887362d-report.zip` records the clean r6 source pair,
four closed stream sessions between 15:51 and 16:00, no launcher failure, no
cleanup errors and no remaining imports. It contains one backend 400 response
at 15:57:59 for a microphone-status query on the **DualShock 4** output created
at 15:57:58. The existing compatibility handler marks that endpoint unavailable
after a 400/404 and preserves unknown/last state. This is not a generic joystick
query and does not establish microphone functionality.

The owner also identified unnecessary initial scrolling in the action dialog
and misaligned RGB sliders. The dialog now measures its actual themed content,
including the joystick explanation and optional live preview, before choosing
an opening size clamped to the current monitor's DPI-adjusted work area. Small
screens retain scrolling. The extras panel is slightly wider, long mouse-option
text wraps, and RGB channels use symmetric colour surfaces with centred tracks.
Headless layout checks cover both themes, a joystick dialog, the optional preview
and smaller viewports; rail centres are checked in both directions.

After these refinements, the full Release/x64 suite passed with 7,384 tests and
twelve existing opt-in skips (7,396 total). Rendered checks included both themes
and confirmed that the fitted full editor has no scrolling when screen space
permits it. The owner subsequently confirmed that the button editor looks right
on their display; the supplied screenshot shows the complete controller artwork,
centred RGB controls and no outer editor scrollbars.
