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

1. Prove Windows enumeration, named axes and neutral through both APIs with a
   separately owned lab backend; obtain eVenent's working profile if available.
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
have not been performed. Do not copy a development VIIPER over the released,
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
