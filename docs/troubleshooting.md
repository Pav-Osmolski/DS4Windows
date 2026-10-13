# Troubleshooting DS4Windows

[Documentation index](README.md) · [Getting started](getting-started.md)

Back up profiles and settings before changing your installation. Change one
setting at a time and note which physical controller, connection and virtual
output reproduce the problem.

## Controller is not detected

1. Confirm Windows sees the physical controller. Test a data-capable USB cable
   and another port before changing drivers.
2. Check Settings > Controllers > Device Options for the controller family. For Switch 2
   Bluetooth, use DS4Windows discovery and association as described in the setup guide.
3. If HidHide hides the controller, ensure this DS4Windows executable is allowed
   to see it. This matters after changing folders or executable names.
4. If Device Manager shows the controller disabled, verify its identity before
   enabling that device. Avoid disabling unrelated HID devices.

## Duplicate input or unexpected keyboard/mouse output

Configure HidHide so the game sees the virtual controller without also seeing
the physical one. Review Steam Input for the affected game and desktop layout:
another mapper can reinterpret the virtual controller as keyboard/mouse input.
Steam's interface changes, so use its current controller settings rather than
the legacy Big Picture checkbox instructions.

For button conflicts, see [Mouse-click troubleshooting](troubleshooting-mouse-clicks.md).

## VIIPER cannot start or no virtual output appears

Read the startup error and follow the requested Install / Repair VIIPER action.
Restart Windows if setup requests it. Keep the app and bundled backend together;
do not substitute a newer USB/IP or unrelated VIIPER binary.

Quit a conflicting VIIPER instance through its tray icon before retrying.
Portable users must extract the complete package, including backend files and
the Lang folder. Repair retains the portable folder; controller output pauses
briefly while an active backend is repaired.

Setup logs are under `%ProgramData%\DS4Windows\Installer`; use Open log or
Copy diagnostics from the installer when available.

## Setup stops after the USB/IP replacement reboot

Driver replacement has two phases: remove the old package, restart Windows, then
run Install / Repair again to verify removal before installing the matching
replacement. A retained device record is not necessarily a live root device;
Windows can keep non-present records after uninstall.

The post-reboot check distinguishes confirmed non-present records from present
or unclassified devices. Running USB/IP drivers or retained driver packages still
block replacement. Do not delete the replacement-state file or force a driver
overlay to bypass this boundary.

If Repair remains blocked, collect **Open log / Copy diagnostics**, especially
`infrastructure-actions.log` under `%ProgramData%\DS4Windows\Installer`.
Include the `USBIP post-reboot device` lines (instance ID, presence and problem
code) and `USBIP post-reboot driver` lines, the previous USB/IP version if known,
and whether Windows was actually restarted. These identify whether the remaining
item is a phantom record, a live device or an incomplete driver removal.

## Haptics, triggers or audio do not work

First confirm ordinary input works. Check the game's controller support,
profile output type, controller audio/speaker settings, physical connection,
and overlapping Steam Input. Restart the game after changing output type.
Disable profile Trigger Lab effects while testing game-authored triggers.
Test native game haptics separately from Audio Haptics capture. Use the
[controller audio guide](controller-audio.md) for endpoint/mute setup and
[compatibility guide](controller-compatibility.md) for hardware limits.

## Updates or profile settings are missing

Use [this fork's releases](https://github.com/Pav-Osmolski/DS4Windows/releases).
Installed users run the matching Setup EXE; portable users replace the complete
packaged app files in their existing folder. Keep profiles, settings and
portable-data. Check both `%APPDATA%\DS4Windows` and the app folder for settings.

RC4.6.7 checks upstream and needs one manual upgrade. Later fork builds use
this repository with DS4Updater 2.0.9 or newer. Replacing the updater alone
cannot change the old app's release source.

## Report a reproducible issue

[Open an issue](https://github.com/Pav-Osmolski/DS4Windows/issues) with:

- App version and full Windows build.
- Controller model, USB/Bluetooth connection and virtual output type.
- Installed or portable setup, and any overlapping mapping software.
- Steps, expected behaviour and observed behaviour.
- Relevant exported log or installer diagnostics, reviewed for personal data.

Enable verbose logging only when needed to reproduce a problem, then turn it off.
For slow profile-editor opening, enable **Settings > Updates & tools > Profile
editor timing logs**. This separate option records `[ProfileEditorTiming]` phases
and is off by default. Turn it off after collecting the log.
Hardware-specific support is not established by a passing unit test alone.
