# DS4Windows user guide

[Documentation index](README.md) · [Install and update](getting-started.md) · [Troubleshooting](troubleshooting.md)

DS4Windows reads a physical controller and presents the virtual controller
selected by your profile. Available settings depend on the controller and connection.

## Controllers

![Controllers overview](images/tour/overview.png)

The Controllers page shows connected devices, battery, connection and selected
profile. Use the profile selector to choose a saved profile, and Edit to change
it. Link a profile to the controller ID when you want that device to reuse it.
Temporary changes and wireless disconnect controls apply to the selected device.

For Joy-Cons, use Link/Unlink or the automatic pairing option. A pair needs a
left and right controller of the same generation. Choose upright or sideways
orientation for a single Joy-Con. Switch 2 Bluetooth discovery and association
are available in Settings; see [Getting started](getting-started.md#connect-and-choose-a-profile).

## Profiles

Create a profile from a preset, then choose the output device the game expects.
Xbox 360 is a useful starting point for XInput games. Choose DualShock 4 or
DualSense when the game supports that controller's features. Restart the game
after changing output type if it does not detect the new virtual controller.

You can rename, duplicate, import and export profiles from the Profiles page.
Back up your settings before deleting profiles or importing replacements.

### Edit a profile

| Page | Purpose |
| --- | --- |
| Button Mapping | Map buttons, sticks, triggers and gestures to controller, keyboard, mouse or macro actions |
| Special Actions | Configure combinations, macros, launches and profile shifts |
| Controller Readings | Inspect live stick, trigger and motion input |
| Axis Config | Adjust dead zones, curves, sensitivity and flick stick |
| Lightbar | Set colour, battery indicators, flashing and passthrough |
| Touchpad | Choose mouse, gestures, controls, absolute positioning or passthrough |
| Gyro | Configure motion aiming, mouse output, steering or passthrough |
| Audio Haptics | Use system or application audio as a haptics source |
| Trigger Lab | Configure persistent adaptive-trigger effects |
| Nintendo Options | Configure supported rumble, motion, calibration and Joy-Con 2 optical-mouse features |
| Advanced | Choose output type and configure rumble, audio, latency and compatibility options |

Save to keep your edits. Back to Profiles cancels editing. Test one change at a
time, especially dead zones, gyro sensitivity and trigger effects.
Trigger Lab effects override game effects on the triggers where they are enabled.
Audio Haptics is separate from game-authored native DualSense haptics.

## Auto Profiles

Associate profiles with applications or window titles to switch automatically.
Check the selected controller slot and matching application when a profile fails
to activate. Verify manual profile selection first before debugging automation.

## Output Slots

Inspect virtual output assignments and use the available plug/unplug controls.
The physical controller is the input; the assigned virtual controller is what
the game receives. A failed disconnect can leave an output registered for retry;
use the log to diagnose it instead of repeatedly adding more outputs.

## Settings

Configure startup, appearance, notifications and update preferences here.
Device Options control controller-family features. Advanced settings include
VIIPER setup and repair, OSC, UDP motion output and diagnostic utilities.

Use [Swipe profiles](swipe-profiles.md) to limit profile cycling. For double
input, configure HidHide and check overlapping Steam Input mappings.
Run at Startup and Start Minimized are separate choices.

## Log and diagnostics

The Log page shows startup, controller, profile and backend events. Export the
log when reporting a reproducible problem. Include app version, Windows build,
controller model, connection and output type. Review exported logs for personal
information. See [Troubleshooting](troubleshooting.md) for the first checks.

## Native feedback and feature limits

For native DualSense feedback, the game must support it, the profile must use
compatible output, and overlapping Steam Input remapping must be disabled for
that game. Controller audio/speaker support is needed for advanced haptics.
Supported feedback differs between controller models and USB/Bluetooth.

Nintendo rumble translation does not add adaptive-trigger hardware. Original
Joy-Cons do not have Joy-Con 2 optical mouse sensors. See the linked feature
guides before assuming a virtual output can reproduce every physical feature.
