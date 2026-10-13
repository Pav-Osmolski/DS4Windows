# DS4Windows user guide

[Documentation index](README.md) · [Install and update](getting-started.md) · [Troubleshooting](troubleshooting.md)

DS4Windows reads a physical controller and presents the virtual controller
selected by your profile. Available settings depend on the controller and connection.

## Overview

![Overview dashboard](images/tour/overview.png)

Start here for a summary of your connected controller, active profile and backend status.

## Controllers

![Connected controllers and profile controls](images/tour/controllers.png)

The Controllers page shows connected devices, battery, connection and selected
profile. Use the profile selector to choose a saved profile, and Edit to change
it. Link a profile to the controller ID when you want that device to reuse it.
Temporary changes and wireless disconnect controls apply to the selected device.

For Joy-Cons, use Link/Unlink or the automatic pairing option. A pair needs a
left and right controller of the same generation. Choose upright or sideways
orientation for a single Joy-Con. Switch 2 Bluetooth discovery and association
are available in Settings; see [Getting started](getting-started.md#connect-and-choose-a-profile).

## Profiles

![Saved profiles](images/tour/profiles.png)

Create a profile from a preset, then choose the output device the game expects.
Xbox 360 is a useful starting point for XInput games. Choose DualShock 4 or
DualSense when the game supports that controller's features. Restart the game
after changing output type if it does not detect the new virtual controller.

You can rename, duplicate, import and export profiles from the Profiles page.

If saving fails, the editor stays open and the last valid profile file is kept.
Check Log for the file error, resolve permissions or free space, and retry.
A rename collision leaves both existing profiles unchanged; the Rename dialog
stays open so you can choose another name. If a save-based rename cannot remove
the original file, both complete copies are kept; check Log before removing any
copy yourself.

Overview quick settings apply live but still need a successful profile save.
If a write fails, Overview shows **Retry unsaved changes** and **Discard pending
saves**. Retry writes the captured changes to their original profile, even if
you have selected another profile. It refuses to overwrite a profile that has
since been edited or deleted. Discard cancels the pending saves without reverting
live settings. Pending saves are held only for this app session; resolve them
before closing DS4Windows. Before editing or renaming a profile with pending
Overview saves, retry or discard those saves first.

Back up your settings before deleting profiles or importing replacements.

### Edit a profile

![Profile editor and mapping navigation](images/tour/profile-editor.png)

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
| Advanced | Use Output, Feedback, Audio, Input and Connection categories to configure output, rumble, controller audio, mouse mappings, latency and compatibility options. Hardware-specific controls follow the selected physical controller. |

Axis Config separates **Left stick**, **Right stick**, **Triggers** and **Motion
axes**. Stick pages group dead zones, response shaping, outer-stick actions and
calibration, with optional delta acceleration and flick-stick settings. Lightbar
groups profile colors, battery alerts, charging and rainbow brightness. Touchpad
and Gyro keep their output-mode selectors, with focused cards for the selected
mode, activation, smoothing and calibration. Changing sections preserves unsaved
edits; use **Apply** or **Save** to write them as usual.

Controller Readings groups live status, sticks, triggers, motion sensors and the
first touchpad contact into cards that wrap to the available width. **Input** is
the physical reading; **Output** is the profile-mapped value. Blue plot markers
show physical input, gray markers show mapped output, and red areas show dead
zones or drift. The observed physical report interval is not end-to-end latency.
Sensor bars are read-only; configure behavior on the other editor pages.

![Profile editor with a DualShock 4 layout](images/profile-editor-current.png)

The controller diagram follows the selected controller layout; this example
shows a DualShock 4 layout, while the earlier image shows a DualSense.

![Button remapping dialog](images/tour/remapping-dialog.png)

Select a mapped control to open the remapping dialog and choose its action.

Save to keep your edits. Back to Profiles cancels editing. Test one change at a
time, especially dead zones, gyro sensitivity and trigger effects.
Trigger Lab effects override game effects on the triggers where they are enabled.
Audio Haptics is separate from game-authored native DualSense haptics.

## Audio Haptics

![Audio Haptics controls](images/tour/audio-haptics.png)

![Audio Haptics source and response settings](images/audio-haptics.png)

The source and response controls let you tailor audio-driven feedback separately
from your button mappings.

Choose a system or application audio source and tune the haptics response. See
[DualSense Bluetooth audio](dualsense-bluetooth-audio-haptics.md) for transport limits.

## Trigger Lab

![Adaptive trigger controls](images/tour/trigger-lab.png)

Build persistent adaptive-trigger effects for supported DualSense controllers.
Enabled Trigger Lab effects take priority over game effects for that trigger.

## Auto Profiles

![Application and window title profile rules](images/tour/auto-profiles.png)

Associate profiles with applications or window titles to switch automatically.
Check the selected controller slot and matching application when a profile fails
to activate. Verify manual profile selection first before debugging automation.

## Output Slots

Use this page to see which physical controller feeds each virtual output slot.
**Slot** is DS4Windows' output slot number, not a game-reported XInput index.
**Current output** shows the virtual controller type. **Reservation** shows
Dynamic allocation or the type reserved permanently. **Input bound** means a
physical controller is feeding the slot.

**Auto-fit columns** uses the available window width. **Fit columns** restores
that layout. Dragging a column edge switches to manual sizing and saves the
widths with your application settings, including across restarts. Turning
Auto-fit off restores your last manual layout. Long names wrap; narrower windows
provide horizontal scrolling and full-text tooltips rather than hiding details.
Select a row to access Plug, Unplug and the existing reservation controls.
A failed disconnect can leave an output registered for retry; use the log to
diagnose it instead of repeatedly adding more outputs.

## Settings

![Settings: General section](images/tour/settings.png)

The image shows the General section of the redesigned Settings page. The checked
options are illustrative; your saved preferences may differ.

Settings fills the available window width and groups related options into cards.

| Section | What you can change |
| --- | --- |
| General | Startup, window behavior, notifications and latency warnings |
| Appearance | App theme, tray icon and language |
| Controllers | VIIPER setup, HidHide, Steam Input recovery, device options and swipe profiles; optional Nintendo pairing |
| Updates & tools | Update checks, profile folder, diagnostic utilities, verbose logging and optional profile-editor timing logs (off by default) |
| Integrations | DSX game mods, UDP motion output and OSC control/monitoring |
| Advanced | Custom Steam folder, executable name, process priority and absolute mouse display |

Search settings to find matching groups across all sections. Use **Ctrl+F** to
focus the search; **Clear**, or **Escape** while typing, returns to the selected
section. Searching keeps your edits. The app remembers the last selected section
when you close it normally.

Language changes show a restart reminder. A custom executable name shows a
re-launch reminder with the filename to open. Theme and tray icon changes apply
immediately.

Use [Swipe profiles](swipe-profiles.md) to limit profile cycling. For double
input, configure HidHide and check overlapping Steam Input mappings.
Run at Startup and Start Minimized are separate choices.

## Log and diagnostics

The Log page shows startup, controller, profile and backend events, including
installed/portable deployment mode, USB/IP status and detected driver versions.
Use **Copy Text** to paste the visible log into an issue, or **Export** to save it. Include app version, Windows build,
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
