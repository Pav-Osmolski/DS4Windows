# Generic joystick development

Status: experimental foundation on `feature/directinput-output` in both
DS4Windows and VIIPER. This is **not yet a selectable Profile Editor output**
and is not included in the current release.

## Implemented foundation

- A pure mapper for already-mapped DS4Windows input; no second physical reader.
- Legacy layout: LX/LY -> X/Y, RX/RY -> Z/Rz, L2/R2 -> Rx/Ry.
- Racing layout: LX/LY -> X/Y, RX/RY -> Rx/Ry, L2/R2 -> Z/Rz.
- Centred, full-range, combined and button trigger modes, with explicit inversion.
- Twelve- or sixteen-button packing and an eight-direction hat with neutral.
- A versioned wire frame matching the backend's named-axis contract.
- Native x86/x64 DirectInput and WinMM read-only probes.

Centred triggers use half an axis's travel; full-range triggers rest at an
endpoint. Combined mode uses RT minus LT, so equal pulls cancel and independent
braking/acceleration are lost. Analog modes do not emit Sony-style digital
trigger followers. Buttons mode uses a configurable threshold (1..255).

The mapper converts the existing byte-resolution mapped state; a 16-bit wire
does not create additional physical stick or trigger resolution. Existing
dead-zone/curve processing must run exactly once before this conversion.
The frontend must pass `Neutral(options).AxisValues` at device creation so
full-range/inverted trigger neutral also applies to backend recovery.

Button numbers are stable: 1 Cross, 2 Circle, 3 Square, 4 Triangle, 5 L1, 6 R1,
7 L2 and 8 R2 in Buttons mode only, 9 Share, 10 Options, 11 L3, 12 R3. In the
16-button layout, 13 is PS and 14 touchpad click; 15/16 remain unassigned.
This is a proposed generic layout, not a claim about any game's default binds.

## Research and implementation stages

[The VIIPER branch specification](https://github.com/Pav-Osmolski/VIIPER/blob/feature/directinput-output/docs/devices/genericjoystick.md)
records the descriptor, wire format, experimental identity and source references.

eVenent's working PadForge setup used three sticks (six axes), no dedicated
triggers and twelve buttons. Its reported compatibility regression was fixed
by moving the second stick to Z/Rz. We reproduce those named axis roles without
claiming a vJoy or Mad Catz identity. See
[PadForge #123](https://github.com/hifihedgehog/PadForge/issues/123).

Next steps:

1. Windows enumeration, named axes and neutral are validated with the isolated
   lab below. Obtain eVenent's working profile if available for game comparisons.
2. Settle the production USB identity and descriptor caching policy.
3. Add output allocation/lifecycle and profile persistence. Reject unsupported
   backend capabilities rather than falling back to another controller.
4. Add a gated **Generic joystick (DirectInput)** output card with layout,
   trigger controls and a live preview. Coordinate profile/output-slot changes.
5. Test original game editions and saved bindings, then produce a matched
   DS4Windows/VIIPER preview. Document actual successes and limitations.

Main branches, released payloads, package hashes, startup tasks and installed
drivers are untouched. A normal app build does not activate the prototype.
Force feedback, extra simultaneous outputs and arbitrary HOTAS layouts remain
outside the first input-only milestone.

## Windows consumer probe

From the repository root, with Visual Studio C++ Build Tools and a Windows SDK:

```powershell
./utils/GenericJoystickProbe/build.ps1 -Architecture x64
./utils/GenericJoystickProbe/build.ps1 -Architecture x86
./utils/GenericJoystickProbe/bin/GenericJoystickProbe-x64.exe
./utils/GenericJoystickProbe/bin/GenericJoystickProbe-x86.exe
```

An optional argument (0..120 seconds) watches DirectInput values. WinMM prints
an initial snapshot: rerun at held diagnostic positions to compare axis roles.
DirectInput is normalized to -32767..32767; WinMM reports its API's unsigned
range. Compare centre and endpoint semantics, not raw numbers between APIs.

The probe filters the backend's experimental IDs FFFF:D10C / FFFF:D110. It
does not install, attach, detach, hide or remove devices. Exit 0 means both APIs
enumerated the device and the performed reads succeeded, 1 means a probe error,
and 2 means one or both APIs did not find it. Enumeration is not game validation.

Automated backend integration tests use localhost sockets and simulated USB/IP
imports; no Windows driver is attached. Actual lab attachment and game testing
were not performed at the foundation stage; native lab results are recorded
below. Do not copy a development VIIPER over the released,
verified backend or disable its integrity checks.

## Foundation validation (2026-10-13)

- 14 frontend mapping cases passed, including a golden wire frame shared with
  the Go backend, neutral/inverted triggers, button thresholds and axis roles.
- Full DS4Windows suite: 7,326 passed; 12 existing hardware opt-in cases skipped.
  An existing Switch 2 callback-timing case failed on the first full run,
  passed in isolation, and passed in the subsequent unchanged-test full run.
- Full VIIPER suites passed with normal and `release` configurations. The
  registered-device attachment fixture now supplies the required experimental
  options; it still tests actual localhost management/USB-IP attachment.
- C# client generation succeeded. Generated sources compile in a separate
  .NET 8 compatibility smoke project. The generator's normal .NET 10 project
  requires the CI SDK; local .NET 8/9 SDKs cannot build that target directly.
- x86/x64 native probes compiled with warnings treated as errors and ran.
  Both reported no attached experimental joystick (exit 2), as expected.
- Synthetic feeder dry-run and prototype backend/feeder builds succeeded.

These results establish protocol and mapping behaviour, not Windows driver
enumeration, physical controller integration or game compatibility.

## Isolated Windows lab (2026-10-14)

The next milestone is an automatic synthetic-input test, before integrating a
selectable Profile Editor output. It checks **DirectInput and WinMM in both
32-bit and 64-bit processes**: six named axes, twelve buttons, hat directions,
both stick layouts, all four trigger modes, producer-loss neutral and reconnect.
The 156 observations are generated from the actual DS4Windows mapping policy,
not a separate copy of its calculations.

Extract the prepared lab ZIP to a fresh writable folder, close DS4Windows,
VIIPER and games, then run **START.cmd** and accept administrator approval.
The launcher uses the already-installed USB/IP **0.9.8.1** runtime. It never
installs a driver or replaces the released backend. No physical controller
input is needed: this first test feeds known synthetic positions.

It refuses existing USB/IP imports, uses separate authenticated localhost
listeners and exclusive configuration, and creates one temporary joystick at a
time. Cleanup detaches only an import matching its private server and allocated
bus/device (plus native port when a receipt is available). An allocation event
also allows cleanup when attachment succeeds but the stream connection fails.
Foreign imports are preserved and cause an incomplete result. The report ZIP
contains readings, source revisions, OS version and logs; its explicit allowlist
excludes the API password. Let the test finish normally; if interrupted or if
cleanup reports a remaining import, restart Windows before returning to the app.

Send the generated **results/*-report.zip**, whether the result is PASS or FAIL.
Passing this lab establishes Windows enumeration and axis semantics; it does
not establish compatibility with a game, a physical controller or force feedback.

Developer tools (from the repository root):

```powershell
./utils/GenericJoystickLab/Test-Helpers.ps1
./utils/GenericJoystickLab/Build-Package.ps1 -OutputDirectory C:/temp/directinput-lab
./utils/GenericJoystickLab/Test-Runner.ps1 -PlansFile C:/temp/directinput-lab/plans.json -OutputDirectory C:/temp/lab-runner-tests
```

Package creation needs Go, .NET 8 and Visual Studio C++ Build Tools; testing the
finished ZIP needs none of those SDKs. `Build-Package.ps1` accepts explicit Go,
.NET and sibling VIIPER repository paths. It records both source revisions and
whether either checkout was dirty. The native probe's `--json` output normalizes
both APIs to signed axis values and labels them in X/Y/Z/Rx/Ry/Rz order.

Ownership/comparison/timeout tests and the launcher simulation perform no driver
operations. The simulation covers successful completion, a probe timeout,
post-attachment stream failure, foreign-import preservation and report privacy.
Native enumeration was subsequently validated by the user-run revision 4 lab;
see the completed milestone below.

The first user-run lab report confirmed neutral enumeration and the complete
six-axis/twelve-button/one-hat capabilities through both APIs and architectures.
It then stopped with a stale producer, before testing motion. The lab now waits
for Windows enumeration before connecting its input stream: asynchronous initial
USB configuration retires any producer acquired too early. The socket integration
test now includes SET_CONFIGURATION followed by neutral polling and stream
creation. Later resets still invalidate the producer; the lab does not silently
reconnect and hide them. The full matrix still needs another Windows run.

The second native report got past startup but read zero at the first stick
endpoint. Its stale-producer message occurred during final detach, rather than
before the movement check. The consumer probe now supports a persistent
`--server` mode: both architectures keep their acquired DirectInput devices
alive for an entire plan, then sample DirectInput and WinMM on request.
Newly acquired devices have a bounded settling interval. The earlier one-shot
probe could sample a consumer's initial state and repeatedly discard it before
input arrived. Lab debug logs now include published/presented states and
scheduler counters twice per second, allowing the next native report to separate
backend delivery from consumer sampling. This revision is not yet proof of
successful native movement or game compatibility.

The third native report passed stick endpoints, buttons and hat directions
through both APIs in both architectures. It reached the first trigger and
revealed a probe conversion error: WinMM V carries Rx and U carries Ry for
this complete six-axis descriptor, while the probe had reversed them.
DirectInput and the backend presentation already agreed on the correct Rx
value. The conversion now uses X/Y/Z/V/U/R with matching per-axis ranges;
both native builds run independent sentinel/range tests during packaging.
This order also matches the primary
[Wine WinMM implementation](https://github.com/wine-mirror/wine/blob/master/dlls/winmm/joystick.c)
for a descriptor containing X/Y/Z/Rx/Ry/Rz. Remaining trigger modes and
disconnect/reconnect were still awaiting a full native run at that revision.

## Native Windows lab validated (2026-10-14)

Revision 4 report `20261014-233817-ea0f280a-report.zip` passed all **156 expected
observations**, independently rechecked against the complete eight-plan matrix:
**312 API comparisons**, with no mismatches, duplicates or missing steps.

- Windows version reported: 10.0.26300.0, with USB/IP 0.9.8.1.
- Both Legacy and Racing layouts; Centred, FullRange, Combined and Buttons
  trigger modes; configured resting positions and endpoint values.
- x86 and x64 DirectInput and WinMM, including stick endpoints, non-trigger
  buttons and hat directions in the Centred plans.
- Producer disconnect returned the configured neutral, and reconnection
  delivered the held trigger, in every layout/mode/architecture combination.
- All temporary imports were removed; no cleanup errors or probe errors.
- Tested DS4Windows source: `4e158868809eaa3c60ddb014bb2a4fc3da2e1ea1`.
- Tested VIIPER source: `01801558b9c52616afaf145290067c57ad2cbeb3`.

The readiness socket probe produces an EOF log, and each deliberate final
native detach retires the active producer, producing a stale-producer log.
These messages occurred outside the successful input checks; they do not
represent failed observations or failed cleanup.

This completes native **synthetic-input** validation on one Windows machine.
Production USB identity, physical-controller output allocation/lifetime,
profile persistence/editor integration and original-game validation remain.
No release, force-feedback, 16-button native layout or game compatibility claim
follows from this result. Both feature PRs remain drafts.
