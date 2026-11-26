# Generic Joystick setup and game testing

The **Generic Joystick** output presents an input-only USB HID joystick to games
using DirectInput or WinMM. It is available in the standard installer and
portable ZIP from DS4Windows 5.0.84 with its bundled VIIPER 0.1.19 backend.
Windows sees **VIIPER Generic Joystick** with USB VID:PID
[1209:D10C](https://pid.codes/1209/D10C/). The device provides six absolute
axes, sixteen buttons and one eight-direction POV hat. It is input-only and
has no XInput, force feedback or audio interfaces. The USB identity applies
to this fixed device and compatible revisions. Profile settings leave the
advertised layout intact.

## Start with a separate profile

Start DS4Windows from the Start menu for an installed copy or run
`DS4Windows.exe` for a portable copy. Create a gamepad profile and select
**Generic Joystick** under **Emulated controller**. In the Profile Editor,
choose the layout and trigger mode, save, and assign the profile. Keep Output
Slots **Dynamic**. Use **Overview > Control Panel** to check the joystick
before binding it in a game.

The normal DS4Windows startup flow verifies and starts the bundled VIIPER
backend. Built-in HidHide uses the same physical-controller hiding policy as
other VIIPER outputs when enabled.
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
| 13, 14 | PS/Guide, touchpad click |
| 15, 16 | Unassigned by default |
| POV | D-pad |

The mapping editor uses canonical controller targets and labels their joystick
default button numbers. Its diagram does not change the selected output type.
Expand **Numbered button assignments** in the joystick settings to reassign or
clear any of the sixteen outputs. These assignments use the already-mapped
controller channels, so unavailable physical inputs remain inactive.

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

Use DS4Windows' built-in HidHide controls to hide the physical controller
from games that would otherwise see both devices. Before launching a game,
check which controller is visible in **Overview > Control Panel**. If you
tested an earlier lab build, Windows may see the registered device as new
and the game may need new bindings.

The 5.0.84 portable test confirmed that DS4Windows applied its HidHide rule
to a USB DualSense and switched to Generic Joystick. Verification from a game
and installed upgrade and repair checks are tracked in the
[release checklist](directinput-release-checklist.md).

## Multiple joysticks and saved bindings

The earlier native Windows lab passed distinct input, buttons/hat isolation,
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

A follow-up with two registered devices over localhost USB/IP confirmed
distinct USB serials and independent input during removal and recreation.
That test used a userspace USB/IP client, so Windows DirectInput GUID retention
with the registered identity remains unverified.

The refreshed r8 matched app preview was also checked by the owner after the
per-controller USB serial change. The owner reported it observably worked fine
following the requested profile-persistence, reconnect and output-switching
follow-up. Report `20261015-172359-df3e2812` recorded clean launcher cleanup, no
remaining imports and no backend WARN/ERROR entries. This single-controller
follow-up does not resolve the multi-controller identifier limitation or validate
normal HidHide integration.
