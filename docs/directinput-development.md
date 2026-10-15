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

## Physical-controller portable preview (2026-10-15)

The feature branch now connects the validated mapper to the existing mapped-input
writer, output allocation and profile storage. Advanced offers an experimental
generic joystick with Legacy/Racing layouts, four trigger modes, inversions and
a button threshold. Changing its immutable options recreates the virtual device;
malformed profile settings prevent activation. Existing output identities retain
their numeric values. The preview requests twelve buttons and no feedback/audio.

Activation is restricted to the existing hash-pinned `--portable-lab` context.
Normal installations cannot activate this output. The backend must advertise the
versioned joystick contract, acknowledge the requested resting axes and report
an accepted Windows USB configuration before the initial producer opens. Native
import closure clears readiness; an initial USB reset does not imply readiness.

Build a matched package with `utils/GenericJoystickPreview/Build-Package.ps1`.
It publishes a self-contained x64 application, builds the branch's VIIPER,
records both source revisions and payload checksums, and validates the package
without launching applications or touching devices. `START.cmd` requests user
elevation and starts only those verified executables, with separate `lab-data`.
The launcher requires installed USB/IP 0.9.8.1, no existing imports/app instances
and exclusive owned listeners on ports 3241/3242. It collects an allowlist of logs
and cleanup results; authentication keys and profiles are excluded from reports.

Follow the package's `README.txt`: create a fresh gamepad profile, select Generic
joystick in Advanced, assign it and test the generic device in `joy.cpl`. Check
all layouts/modes, saved profile choices, physical unplug/reconnect and switching
back to an existing output. Exit from the tray menu to finish and collect logs.
Keep Output Slots dynamic for this first preview. Portable lab mode does not
configure HidHide, so the physical device remains visible. Game device selection
and hiding need separate coordinated tests.

These implementation and local automated checks do not constitute physical
controller or original-game validation. Production USB identity, force feedback,
sixteen-button native validation and release packaging remain outside this stage.

## Physical-controller and game results (2026-10-15)

The owner tested a USB DualSense with the matched r2 preview (DS4Windows
`6c2760c100f04e515ec7458c0b7db565ff26c8e9`, VIIPER
`bd8f6e21215b471015ca442945c2c7fc90f1cab4`). They confirmed all eight
layout/trigger combinations in `joy.cpl`, inversion and button threshold controls,
saved profile settings, physical unplug/reconnect and service Stop/Start.

| Game | Configuration | Result |
| --- | --- | --- |
| BeamNG.drive 0.39.4.0.20972 | Racing + Full range; X steering, Rz throttle, Z brake | User confirmed hardware response, vehicle bindings and successful driving. |
| FlatOut 2, current Steam installation (exact build not recorded) | Legacy + Centred; X steering, Ry throttle, Rx brake | User confirmed driving and automatic transmission. Analogue menu navigation felt tricky; screenshot showed 0% in-game dead zone. |

Report `20261015-123537-0ff0761f-report.zip` records fifteen successful
attachments/removals with clean final cleanup. The play-session report
`20261015-124824-f68ac364-report.zip` spans about thirty minutes, with 3,570
sampled joystick states, three closed device sessions, no backend WARN/ERROR
events and no remaining native imports. INFO-level EOF messages coincide with
device removal. These reports record backend delivery and lifecycle; gameplay
success and menu behaviour are the user's observations, not automated assertions.

This is compatibility evidence for those tested configurations on one machine,
not proof for every game or older unpatched FlatOut 2 builds. Steam Input/HidHide
state was not independently captured. Production identity and release readiness
remain unresolved.

Testing also exposed an Overview omission: the joystick profile left the quick
output selector blank and its summary said no emulated device. Overview now
recognizes the joystick, keeps its custom profile options when switching output,
and enables the preview choice only in the pinned portable lab context.

The r3 Overview addition accidentally based menu items on the older generic
theme style, losing the shared Overview template and spacing. The corrected
preview retains the `BridgeDropDownItemStyle` template and the original profile
dropdown padding/margins while adding the enabled-state binding. Regression
checks compare those effective properties against the unchanged shared style.
Preview packaging now also composes and validates the normal `Lang` satellite
layout, and refreshes its file-ownership list after moving the translations.

## Normal-release preparation (2026-10-15)

The next stage is tracked in the [release checklist](directinput-release-checklist.md).
The tested twelve-button prototype is the first release target. A concrete
pid.codes request was submitted with owner authorization as
[PR #1317](https://github.com/pidcodes/pidcodes.github.com/pull/1317) on 2026-10-15.
It remains pending approval; its proposed identity must not be used yet. The optional
sixteen-button descriptor remains lab-only and requires a separate identity.

Normal automatic HidHide already recognizes the joystick through the common
VIIPER output path. Its behaviour remains untested for this output because the
isolated preview deliberately bypasses system integration. Production identity,
normal matched packaging and that validation must precede lifting the gate.

The [integration audit](directinput-integration-audit.md) records the reviewed
selectors, status, profile lifetime, backend compatibility and packaging paths.
It found and fixed missing generic-joystick mapping labels and added an
explanation of the binding diagram's canonical targets. No descriptor, mapper,
protocol or release-gate change is part of that audit.

The [preview guide](directinput-preview-guide.md) describes the axis layouts,
trigger modes, button numbers, tested game configurations and device-visibility
limits for testers using the matched package.

### Per-controller identity and synthetic multi-controller lab

The identity audit found that generic joysticks shared one USB serial. Each
instance now exposes `VIIPER-GJ1-<instanceId>`. DS4Windows derives the 32-hex-digit
ID from the physical controller's existing profile-link identity, independently
of profile settings, input slot or connection order. A hashed HID path is the
fallback; that fallback may change across ports. Duplicate physical identities
are rejected rather than silently sharing an output. Recovery checks the
backend's identity echo as well as its neutral-state contract. Existing game
bindings may need to be recreated once after this serial change.

`utils/GenericJoystickLab/Build-Package.ps1 -MultiController` builds a separate
Windows lab. Its START.cmd creates two synthetic joysticks, sends distinct
inputs, disconnects one producer, recreates devices and reverses creation order.
Fresh x86/x64 probes verify DirectInput instance GUIDs and DirectInput/WinMM
readings. WinMM numeric slots are allowed to reorder. The runner records failed
observations and removes only imports proven to belong to its allocated buses.

Local validation: 7,392 DS4Windows tests passed, 12 existing opt-in tests skipped;
VIIPER's full Go suite passed. Native probe builds/self-tests, lab ownership and
failure-path checks, and synthetic pair matching/lifecycle checks passed.
**The new Windows two-device run is pending owner validation.** This substitutes
for a two-physical-controller test only at the virtual-device boundary. Physical
USB/Bluetooth reconnects, normal HidHide and saved game bindings remain separate
checks. Preview identities and the production activation gate remain unchanged.


The first two-device owner run (`20261015-163319-aa0aad5a`) enumerated two
separate DirectInput GUIDs in x86/x64. The x64 distinct-input observation matched
both DirectInput devices, but WinMM's second device remained neutral. The run
stopped before recreation/order tests and reported no cleanup errors. This is
not a multi-controller pass. The follow-up probe polls each WinMM device for a
bounded 500 ms in the same process before reporting its final sample, testing
the cold-reader hypothesis without relaxing any expected input or identity
checks. Backend and controller mapping are unchanged for that retest.

The r2 owner run (`20261015-163623-a67782e1`) passed both architectures through
separate sticks, buttons/hat isolation, neutralization of A after producer loss,
and producer reconnect. After A was removed, B retained its correct input in
both APIs, but x64 DirectInput reported B with A's previous instance GUID. The
run stopped there, with no cleanup errors; recreation/reverse order remain
unvalidated. This is an identity-persistence failure, not an input-isolation
failure. The r3 diagnostic reader records the HID interface path and USB serial
via read-only Windows metadata queries. Identity-only failures remain failed
observations and force a final failure, while permitting later recreation/order
observations to run. Input/enumeration/API failures still stop immediately.
No controller/backend behavior was changed to hide or work around this result.

The completed r3 owner run (`20261015-164334-2482c1b4`, Windows build 26300)
collected all 22 planned observations. Twenty had no errors. Two (x64 and x86
at "Remove A while B remains held") failed DirectInput GUID persistence.
All expected input patterns, capabilities and device counts matched, including
producer reconnect, A recreation and reversed creation order; cleanup had no
errors. This is a complete diagnostic run with an unresolved compatibility
failure, not a full pass.

The HID evidence distinguishes controller identity from DirectInput GUIDs:

- A always returned serial `VIIPER-GJ1-11111111111111111111111111111111`
  and the HID instance path component `3&1ba4aecc&0&0000`.
- B always returned serial `VIIPER-GJ1-22222222222222222222222222222222`
  and path component `3&296b048d&0&0000`, including while A was absent.
- With both present, A's raw-byte GUID key was
  `902A4ABF25C8F1118001444553540000` and B's was
  `20F10323B6C8F1118001444553540000`.
- With only B present, B retained its serial/path/input but was reported under
  A's former GUID key. Recreating A restored the original GUID associations.
  The final reversed-order test also returned the original associations.

Thus distinct USB serials reach Windows correctly and the HID paths remained
stable throughout this particular run. Do not infer general game-binding
persistence or blame a particular Windows/driver component from this alone.
Next investigation should compare enumeration GUIDs with GetDeviceInfo and
Windows joystick configuration/PnP metadata while B remains attached, using
read-only queries, before changing backend identity or registry configuration.
No further identical owner rerun is needed to establish this finding.


### Opened-device identity diagnostic (r4)

The next matched lab adds independent read-only queries on each opened
DirectInput device: `GetDeviceInfo` for its instance GUID and `GetProperty`
with `DIPROP_JOYSTICKID` for its joystick slot. `instance` remains the GUID
copied from the enumeration callback before nested DirectInput calls; the new
`openedIdentity` object records `instance`, `joystickId`, `deviceInfoResult`
and `joystickIdResult`. Unavailable query values are JSON null with the failing
HRESULT retained, never a fabricated slot zero. The persistent probe also
queries its opened objects when taking a snapshot.

Interpretation for the next owner run:

- If enumeration and opened-object GUIDs disagree for the same serial/path,
  investigate that lookup discrepancy before attributing it to backend identity.
- If both GUIDs change together and the joystick slot also changes, the
  reassignment is visible in independent DirectInput queries. That still does
  not establish which Windows/driver component causes it, or prove every game
  loses its saved bindings.
- Stable serial/path with changed GUID remains a recorded identity failure.
  Existing pass/fail criteria, continuation rules and cleanup are unchanged.

Native x86/x64 builds and no-device self-tests validate JSON serialization,
normalization, separate enumeration/opened identifiers, and the distinction
between unavailable metadata and a valid slot zero. The package builder runs
these checks before creating its manifest. Lab matching, lifecycle and cleanup
helper tests also pass. This is diagnostic instrumentation, not a production
fix or a claim that GUID persistence is now validated.

Microsoft references: [GetDeviceInfo](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417896%28v%3Dvs.85%29)
and [GetProperty](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee417908%28v%3Dvs.85%29).

### Completed opened-device comparison (r4)

Owner report `20261015-165810-0ed8a66d` completed all 22 observations on Windows
build 26300 with no cleanup errors. The two failures remain x86/x64 GUID
persistence while A is absent; all input/capability/count comparisons passed.
Every `GetDeviceInfo` and `DIPROP_JOYSTICKID` query returned success (HRESULT 0).
Enumeration and opened-device GUIDs agreed in every reading:

| State | B serial/path | B enumeration and opened GUID key | B joystick ID |
| --- | --- | --- | --- |
| A and B present | Unchanged, B identity | `20F10323B6C8F1118001444553540000` | 1 |
| A removed, B still attached and held | Unchanged, B identity | `902A4ABF25C8F1118001444553540000` (A's former GUID) | 0 |
| A recreated | Unchanged, B identity | Original B GUID restored | 1 |
| Both recreated in reverse order | Unchanged, B identity | Original B GUID | 1 |

This rules out a simple discrepancy between the enumeration callback and our
opened-object metadata. It confirms GUID reassignment alongside slot compaction
through independent successful DirectInput queries, with B's HID identity and
input intact. It does not identify the responsible Windows/driver component or
prove how a particular game handles a saved GUID. No unconditional production
multi-controller binding guarantee is justified and the identity failure must
not be relabelled as a pass.

Further identical owner reruns are unnecessary. Next engineering work is to
trace the Windows joystick configuration/driver mapping, and compare an
unrelated two-joystick setup if available, before deciding whether a supported
fix exists. Retaining a neutral virtual device across physical disconnects is
only a possible mitigation requiring separate lifetime/cleanup design; it does
not solve deliberate output removal. Do not change user registry mappings or
keep hidden placeholder devices merely to make this test pass.

The [Windows identity investigation](directinput-identity-audit.md) compares the
completed r4 report with read-only calibration entries and the pinned USB/IP
attachment code. It records the documented API limits, alternatives considered
and the recommendation to continue preview preparation without waiving the
unresolved production multi-controller acceptance item.

### Refreshed matched app preview (r8)

Release preparation on 2026-10-15 confirmed both feature branches contain their
current main branches, pid.codes #1317 is still open without review, and USB/IP
0.9.8.1 remains the latest published release. The upstream serial fix is now an
explicit controlled-retest item in the identity audit and release checklist.

The 196 focused generic-joystick, HidHide, portable-lab and readiness tests passed
with no skips. These are automated checks, not proof of normal installed HidHide
behavior. A refreshed matched physical-controller preview carries the current
per-controller identity and backend-echo checks alongside normal Lang packaging.
It retains the preview gate, installed USB/IP 0.9.8.1 and isolated lab behavior.
The requested follow-up is limited to profile save/reopen, physical reconnect and
existing-output switching after the serial change. Game/layout matrices already
passed need not be repeated. Normal installer activation and HidHide acceptance
remain open until the production prerequisites are satisfied.

### Owner follow-up on matched physical preview r8

Report `20261015-172359-df3e2812` identifies clean DS4Windows revision
`d78d0cdd6a06479e394cfe00c922c0df5858e85c` and VIIPER revision
`948855d83f25ecdf6f166adc54ca44040b14d240`. After being asked to check profile
save/reopen, physical reconnect and Xbox 360/generic output switching, the owner
reported that it observably worked fine. This is the owner's functional result;
logs alone do not prove each UI action or saved profile value.

The launcher summary has no failure, no cleanup errors and no remaining USB/IP
imports. Backend logs contain five native attachments, five stream ends and five
bus removals between 17:24 and 17:26 BST, with zero WARN/ERROR entries. Both error
streams are empty. Application stdout records the physical read failure at
16:25:35 UTC (17:25:35 BST), consistent with the requested unplug test, followed
by another attachment. It also contains access-denied CreateFile messages while
discovering input-driver interfaces, followed by successful discovery for both
usage types; these messages are retained in the evidence rather than described
as an entirely error-free stdout log.

The refreshed per-controller-identity preview therefore has positive owner
feedback and clean output cleanup. This does not clear the separate synthetic
multi-controller GUID failure, establish normal HidHide behavior (still bypassed
in the lab), or change the pending production identity/upstream driver retest.
No further identical single-controller run is needed on this evidence.

### Normal package composition proof

The [normal packaging preparation](directinput-normal-packaging.md) records the
new preview-isolation/backend-pin checks, real portable and installer composition
proof, upgrade-ownership regressions and remaining promotion work. Full local
application validation passed 7,393 tests with 12 existing opt-in skips. The
normal proof retains released VIIPER 0.1.11 and refuses generic-joystick activation;
it must not replace the matched feature backend from the working r8 preview.
