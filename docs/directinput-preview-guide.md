# Generic joystick preview guide

This experimental output presents an input-only USB HID joystick to games using
DirectInput or WinMM. It is available only in the matched portable preview on
`feature/directinput-output`; it is not yet a normal-installation feature.
It provides six axes, twelve buttons and a POV hat. It provides no XInput
emulation, game force feedback or audio interfaces.

## Start with a separate profile

Use the preview package's `START.cmd` and follow its included `README.txt`.
Create a new gamepad profile, select **Generic joystick (preview)** in the
Profile Editor's emulated-controller selection, choose the layout and trigger
mode, save, and assign the profile to your physical controller. Keep Output
Slots **Dynamic**. Use `joy.cpl` to confirm the joystick responds before binding
it in a game. Fully exit through the tray menu to collect the preview report.

The launcher isolates app data and verifies the matching backend. Do not copy its
backend into an installed release or substitute a different VIIPER executable.
Changing layout or trigger settings recreates the virtual device; do this before
starting a game that might retain its old device connection.

## Choose axis roles and resting positions

| Profile layout | Left stick | Right stick | L2 / R2 axes |
| --- | --- | --- | --- |
| Legacy | X / Y | Z / Rz | Rx / Ry |
| Racing | X / Y | Rx / Ry | Z / Rz |

The names are Windows joystick axes, not an assertion that a physical wheel or
three physical sticks are connected.

| Trigger mode | Released position and behaviour |
| --- | --- |
| Centred | Each trigger rests at the axis centre and moves towards its positive endpoint. |
| Full range | Each trigger rests at the negative endpoint and spans the complete axis range when squeezed. |
| Combined | Both triggers share the first trigger axis: R2 minus L2. Equal pressure cancels to centre; the other trigger axis stays centred. |
| Buttons | Triggers produce buttons 7 and 8 at the configured threshold; their analogue axes remain centred. |

Inversion changes the corresponding axis direction. Combined mode has its own
inversion instead of separate L2/R2 inversions. The threshold applies to Buttons
mode. Save these choices in the profile; game bindings may need to be redone
after changing axis roles or resting positions.

## Button targets

| Buttons | Default targets |
| --- | --- |
| 1, 2, 3, 4 | Cross, Circle, Square, Triangle |
| 5, 6 | L1, R1 |
| 7, 8 | L2, R2 in Buttons trigger mode |
| 9, 10 | Share, Options |
| 11, 12 | L3, R3 |
| POV | D-pad |

The mapping editor uses canonical controller targets and labels their joystick
button numbers. Its diagram does not change the selected output type. Guide/PS
and touchpad click have no output button in this twelve-button descriptor;
remap their physical inputs to a supported target or keyboard action if needed.

## Configurations already tested by the owner

| Game | Profile and bindings | Observed result |
| --- | --- | --- |
| BeamNG.drive 0.39.4.0.20972 | Racing + Full range; X steering, Rz throttle, Z brake | Hardware response, binding and driving passed. |
| FlatOut 2, current Steam installation; exact build not recorded | Legacy + Centred; X steering, Ry throttle, Rx brake | Driving passed with manual and automatic transmission. Stick menu navigation was awkward with the game's dead zone at 0%. |

These are tested starting configurations on one machine. Original unpatched
editions and other legacy games are not yet verified. For a new game, record
the edition/patches, joystick selected, binding names, profile layout/mode,
Steam Input state and whether the physical controller is also visible.

## Physical-device visibility and remaining limits

The app normally has built-in HidHide integration. The isolated preview
deliberately bypasses it, so its tests do not prove automatic physical-device
hiding. A game may see both the real controller and the virtual joystick;
confirm which one it is using when reporting results. No separate hiding
configuration is automatically applied by this launcher.

The USB identity is still experimental. The requested production identity is
[pending allocation](https://github.com/pidcodes/pidcodes.github.com/pull/1317).
When it changes, Windows and games can see a new device and require new bindings.
Sixteen buttons, force feedback and arbitrary HID descriptors are outside this
preview. See the [release checklist](directinput-release-checklist.md) for the
remaining normal-installation validation.

## Multiple joysticks and saved bindings

The synthetic two-device test passed distinct input, buttons/hat isolation,
producer reconnect, device recreation and reversed creation order in x86/x64
DirectInput and WinMM. USB serials and HID paths remained distinct and stable
through that run. However, removing the first joystick caused the remaining
joystick to move from slot 1 to slot 0 and take the first joystick's former
DirectInput instance GUID. Enumeration and the opened device both reported the
change. Recreating the first joystick restored the original associations.

This is a known unresolved compatibility limitation on the tested Windows build
26300, not a guarantee of the same behavior on every machine. Games that rely
on saved joystick identifiers may need their controller selection or bindings
checked after output devices are added or removed. Configure the intended
outputs before launching a game. No saved game-binding test has yet established
whether a particular title is affected. See the
[development evidence](directinput-development.md#completed-opened-device-comparison-r4).

The refreshed r8 matched app preview was also checked by the owner after the
per-controller USB serial change. The owner reported it observably worked fine
following the requested profile-persistence, reconnect and output-switching
follow-up. Report `20261015-172359-df3e2812` recorded clean launcher cleanup, no
remaining imports and no backend WARN/ERROR entries. This single-controller
follow-up does not resolve the multi-controller identifier limitation or validate
normal HidHide integration.
