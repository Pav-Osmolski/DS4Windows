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

### Controller stays hidden after Stop or exit

Normal **Stop** or tray-menu **Exit** releases DS4Windows-managed hiding rules,
including rules retained by **Keep controllers hidden between reconnects**.
Closing the window may only minimize the app, depending on your settings.

A crash, forced termination or older build can leave persistent HidHide rules
behind. Builds with ownership recovery restore confirmed abandoned rules at
Start. For older or untracked rules, use **Stop**, then **Settings > Components >
Recover hidden controllers…**. Select only your physical controller's entries
and choose **Restore selected controllers**. Nothing is selected automatically.
Unselected rules and application permissions are preserved, with a backup saved
before changes. Reconnect the controller if Windows has not refreshed its list.

There is no need to uninstall HidHide to perform recovery. If another DS4Windows
session owns a rule, stop that session too. Rules created by another mapper or
a manual setup require explicit selection because DS4Windows cannot safely
assume they are abandoned. See [HidHide recovery](hidhide-device-lifecycle.md).

### A second Bluetooth controller will not connect

Check **Windows > Bluetooth & devices** during the failed attempt. If Windows
never shows it connected, record that separately from a controller Windows
connects but DS4Windows cannot open. With Steam and DS4Windows fully exited,
compare both connection orders, then repeat with DS4Windows running. Note
whether VIIPER remains running and capture a verbose log of the app-running test.

In [issue #95](https://github.com/Pav-Osmolski/DS4Windows/issues/95#issuecomment-6525380415),
the later tests also failed with DS4Windows exited. A different adapter worked
in the reported comparison, but that test also included a reboot and fresh
pairing. It was a workaround, not proof of faulty hardware, a particular
power-saving setting or a confirmed app fix. Do not assume the same cause for
every second-controller failure.

## Touchpad mouse stops with Game Bar or the Gamepad touch keyboard

First compare touchpad mouse movement on the desktop, with Game Bar or the
Windows touch keyboard's **Gamepad** layout open, and again after closing it.
Record whether controller buttons also stop reaching DS4Windows. Repeat over
USB using the same profile; a working USB test does not rule out a Bluetooth
interaction.

In [issue #107](https://github.com/Pav-Osmolski/DS4Windows/issues/107), a local
DualSense test showed Windows temporarily withholding streaming Bluetooth input
while a gaming overlay was open. Touchpad mapping remained enabled. Direct
current-state requests still returned changing, valid controller reports. This
explains that captured freeze; it does not establish a cause for every Game Bar
or Bluetooth problem, including the reporter's separate USB observation.

Builds containing the #107 fix keep the usual streaming reader and use a bounded
current-state fallback during interruptions on supported Sony DualShock 4,
DualSense and DualSense Edge Bluetooth identities. They validate checksums and
reject older queued state when streaming resumes. Unsupported devices and USB
retain their usual input path. Current-state sampling cannot recover a history
of very short presses that occurred between samples. The local physical test
used DualSense; DS4/Edge paths also have synthetic regression coverage.

The default-on **Keep Bluetooth controller input working in Game Bar** checkbox
in **Settings > Controllers > Connection and device options** controls this
recovery. Changes apply after **Stop/Start**; reconnects within the same running
session keep the previous choice. Turning it off restores the original input
reader without current-state queries. This is separate from **Profile Editor >
Advanced > Output > Game Bar**, whose optional controller compatibility routes
mapped input through an Xbox-compatible companion for overlay navigation.

If the problem persists, attach the affected **profile XML** (for example
`Pc.xml`, rather than application settings `Profiles.xml`) and a **Verbose** log
including startup, the failure time and the keyboard/mouse output handler.
State the physical controller, connection, virtual output, Windows build and
whether a physical mouse still works. An old Bluetooth-pause warning alone does
not prove that it coincides with the touchpad problem.

## Switch 2 controller is reached but its slot cannot activate

Starting in 5.0.31, an unassigned Switch 2 input slot loads the existing
**Default** profile. It does not create or overwrite that profile. Explicitly
selected or linked custom profiles are never silently replaced.

The log now identifies the input slot, profile selection source and profile
name. `failure=Missing`, `Invalid` or `Unreadable` means profile preparation
failed. Restore the selected profile, fix its contents/access, or correct its
saved/linked selection before reconnecting. An unassigned slot also needs a
valid `Default` profile in the active settings folder. Back up settings before
editing saved selections; deleting all settings or re-pairing is unnecessary
for a profile-loading failure.

`virtual-output-unavailable` means the profile loaded but its requested virtual
controller could not activate. Include the preceding VIIPER/output errors in
your report. This is separate from Bluetooth association; the association is
preserved. Wake and retry after correcting the cause and completing cleanup.

If you migrated from another fork or merged an old backup, first review the
[migration guidance](getting-started.md#moving-from-another-fork). In
[issue #48](https://github.com/Pav-Osmolski/DS4Windows/issues/48#issuecomment-6252913392),
the reporter resolved their problem with a clean install after mixing very old
settings; the exact conflicting configuration could not be reproduced. The
profile-loading safeguards above remain useful independently of that report.

For a new activation-failure report, include the full log from startup through
failure, app version, the affected
Joy-Con half, selected profile and whether **Link profile** or automatic
profiles are enabled. Test each half alone, both connection orders, and a few
sleep/wake reconnects. Share successful results too. A full log is always best:
earlier startup/profile/backend lines can explain the final rejection. For this
test, enable **Log > Verbose logging**, fully exit/restart DS4Windows and
reproduce once. Use **Log > Copy Text** to paste the whole retained log from
startup through failure, then disable verbose logging. If startup has left the
1,000-entry view, or extra saved diagnostics are needed, use **Open log folder**
and attach `ds4windows_log.txt`. The saved file can contain additional markers.
This release addresses
reproduced profile failures; it does not establish the cause on every machine.

## Duplicate input or unexpected keyboard/mouse output

Configure HidHide so the game sees the virtual controller without also seeing
the physical one. Review Steam Input for the affected game and desktop layout:
another mapper can reinterpret the virtual controller as keyboard/mouse input.
Steam's interface changes, so use its current controller settings rather than
the legacy Big Picture checkbox instructions.

For button conflicts, see [Mouse-click troubleshooting](troubleshooting-mouse-clicks.md).

If a game loses its controller when you Alt+Tab, check **Auto Profiles** as well
as the active profile's **Emulated Controller**. Switching between Xbox and
DualSense output when the game loses focus recreates the virtual controller.
Keep output types consistent when the game needs the same device throughout
the session; [issue #95's follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/95#issuecomment-6525380415)
resolved this separately from the Bluetooth connection problem.

## VIIPER cannot start or no virtual output appears

Read the startup error and follow the requested Install / Repair VIIPER action.
Restart Windows if setup requests it. Keep the app and bundled backend together;
do not substitute a newer USB/IP or unrelated VIIPER binary.

### Connected, Select a profile, or Needs attention

If **Select a profile** is shown, open **Controllers > Active Profile** and
choose the intended profile. Create one under **Profiles** first if the list is
empty. Physical input can be present while no virtual controller is created
because no profile is selected. This was one of the separate findings in
[issue #66](https://github.com/Pav-Osmolski/DS4Windows/issues/66#issuecomment-6347132764).

With a profile selected, read the **Controller status** explanation and compare
the virtual device in `joy.cpl`. Update the complete app/backend package before
repeatedly reinstalling drivers. Intermittent VIIPER confirmation/connection-check
failures in [#66](https://github.com/Pav-Osmolski/DS4Windows/issues/66#issuecomment-6381990559)
were addressed in **5.0.47** with its matching backend; that report did not prove
a laptop hardware, Windows-version or antivirus fault. For a recurrence, capture
the failed attempt with both application and VIIPER debug logs before restarting.

### Stop or reconnect gets stuck

Update the complete package first. The DS3 Stop/Start and reconnect defects in
[#25](https://github.com/Pav-Osmolski/DS4Windows/issues/25#issuecomment-6166743185)
were confirmed resolved in **5.0.23**; no controller-driver reinstall was needed.
For a new failure, distinguish **Stop while connected** from **disconnect, then
reconnect**, note whether the window still responds, and capture the full verbose
log through shutdown. The last visible cleanup message alone does not establish
which cleanup step stalled.

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

### Triggers work, but native DualSense speaker/haptics are silent

A game can send trigger/lightbar commands without sending its separate audio
haptics. For a game with native DualSense support, fully exit the game, select
**Disable Steam Input** in its Steam controller override, start DS4Windows and
wait for **Ready**, then relaunch the game. Keep the virtual controller playback
device enabled and your usual speakers/headphones as the default main output.
This resolved [Ratchet & Clank in issue #103](https://github.com/Pav-Osmolski/DS4Windows/issues/103#issuecomment-6560777205).

If input stops working with Steam Input disabled, report that detection problem
rather than assuming every game requires the same setting. In a new report,
include the game, active profile, USB/Bluetooth connection and full logs; check
whether the virtual controller playback meter moves. A successful Windows
speaker test or adaptive trigger effect alone does not verify game haptics.

### Microphone is listed but silent, or a recording contains game echo

Check the recording app's **exact selected input**, Windows microphone privacy
permissions, and the profile's microphone-enable/mute settings. The physical
controller microphone and **DS4Windows Controller N Microphone** are distinct
devices. Forwarding starts when a recording client opens the virtual input;
an idle input is not a failed listening test. A Windows microphone test result
of **0%** means no level was recorded, not necessarily that input volume is 0.
See the [microphone routing guide](controller-audio.md).

For echo, record system sound and microphone on **separate tracks**. Keep game
audio playing, stay quiet briefly, then speak. Inspect the microphone track on
its own. Check **Listen to this device**, monitoring/loopback settings and the
selected input before changing controller drivers. If game audio is on the
microphone track, compare a second compatible headset directly connected to the
same controller, if one is available. In
[#90](https://github.com/Pav-Osmolski/DS4Windows/issues/90#issuecomment-6551225577),
changing headsets removed the unwanted audio; that result does not establish
that every echo report is a hardware fault.

For a silent DS4 Bluetooth microphone on older builds, update first:
[#80](https://github.com/Pav-Osmolski/DS4Windows/issues/80) identified a separate
virtual-microphone status request defect, subsequently fixed and confirmed by
the reporter. Share both logs during an actual recording attempt if it persists.

### Headphone routing or battery-alert previews are silent

For managed controller audio, enable **Stream audio to controller**, select the
intended source, and use **Audio output > Follow headphone jack** to switch
between speaker and connected headphones automatically. This covers managed
DualSense/Edge USB/Bluetooth and genuine DS4 Bluetooth routes; DS4 USB audio is
handled by Windows. An Audio Haptics route override can retain a different
choice ([#74](https://github.com/Pav-Osmolski/DS4Windows/issues/74)).

**Battery alerts** are off by default under **Settings > Controllers**. For a
silent preview, check its selected output, speech/sound selection and volume;
a specifically selected missing device is reported rather than replaced
silently. Controller-speaker previews also require an enabled, unmuted speaker
route in **Profile Editor > Advanced > Audio**. Controllers without a supported
speaker can use PC/headphone audio. See [battery alerts](user-guide.md#battery-alerts)
for Windows voices, supported sound files and repeat settings added through
[#45](https://github.com/Pav-Osmolski/DS4Windows/issues/45). Preview/on-demand
checks do not count toward automatic reminder totals.

### DualSense haptics stop after disabling a Windows audio device

Keep **DS4Windows Controller N Audio** enabled in Windows Sound when using
virtual DualSense or DualSense Edge output, over USB or Bluetooth. Native game
haptics use the actuator channels of this virtual playback device; disabling it
also prevents those haptics from reaching DS4Windows. Ordinary buttons and sticks,
and manual rumble tests, can still work.

1. Open **Windows Sound settings > More sound settings > Playback** (or run
   `mmsys.cpl`). Right-click the device list and enable **Show Disabled Devices**.
2. Enable the **DS4Windows Controller N Audio** device for the affected controller.
3. Keep your usual headphones or speakers selected as both **Default Device** and
   **Default Communications Device**. The controller playback device does not need
   to be either default for game-authored haptics. If the game has a separate
   controller-audio selector, keep it assigned to the matching controller device.
4. Restart the game if it does not rediscover the enabled playback device.

The **DS4Windows Controller N Microphone** recording device is separate. It can
remain disabled when you do not use controller microphone forwarding; it is not
required for game haptics. Do not disable the playback device to silence or avoid
using the microphone.

The missing haptics in [issue #98](https://github.com/Pav-Osmolski/DS4Windows/issues/98#issuecomment-6578485972)
were resolved by re-enabling playback. The before/after capture used the same
profile and showed rear-channel haptics returning with playback enabled. No
Bluetooth adapter replacement, driver reinstall or power-setting change was
needed for that case.

### Delayed or weak DualSense vibration

Update to **5.0.34 or later**, including its matching bundled VIIPER backend.
The delayed haptics problem reported in
[issue #47](https://github.com/Pav-Osmolski/DS4Windows/issues/47) was fixed through
changes in both DS4Windows and VIIPER. Maintainer testing confirmed normal
feedback in DEATHLOOP and GTA V Enhanced over USB and Bluetooth, and
[the original reporter also confirmed the fix on both connections](https://github.com/Pav-Osmolski/DS4Windows/issues/47#issuecomment-6274412583).
The investigation also covered the related upstream
[issue #119](https://github.com/hbashton/DS4Windows/issues/119). See the
[validation record and reporter confirmation](validation/dualsense-native-command-pressure-test.md#reporter-confirmation-after-release).

If feedback still feels delayed or weak, check the profile's rumble strength
and disable local Trigger Lab effects while comparing game-authored feedback.
Test native game haptics separately from Audio Haptics. For a useful report,
enable **Log > Verbose logging**, restart DS4Windows and
play for 1–2 minutes, including the problem and releasing all controls. Use
**Log > Copy Text** to paste the whole retained log and label it **USB** or
**Bluetooth**. Include the affected profile and roughly when the problem occurs.
The full log is always best. If startup is no longer visible, use **Open log
folder** and attach the saved log too. Turn verbose logging off after testing.

#### Reading the feedback diagnostics

In verbose logs, `DualSense native feedback` lines report the physical queue's
pending/high-water counts, admission retries, queue/head age, owner processing
time, completed commands and explicit motor-stop receipt revisions. These reach
the copyable Log page even while controller-speaker streaming defers larger
writer statistics. `USB-write` means a physical HID write completed;
`BT-owner-handoff` means the Bluetooth transport accepted ownership, not that a
radio write or actuator effect has completed. Timings are lifetime maxima for
the physical device; they are not vibration-strength measurements. Profile
and other local overrides can affect the final output.

The released fixes combine corrected feedback pacing in VIIPER with reduced
redundant queue traffic in DS4Windows. Only consecutive identical, unclaimed,
recognized state reports can be suppressed. Since 5.0.34, this includes
documented DualSense power settings on USB and Bluetooth and reports waiting
in the physical-output queue.
`nativePhysicalPendingRepeats` counts redundant pending occurrences shared in
that queue; `nativeRepeatsSuppressed` counts sharing in the earlier VIIPER queue.
Neither counter counts discarded distinct effects. Alternating rumble/trigger
commands, changed values, final stops, media, unknown commands and ownership
changes retain their order.

See [Log and diagnostics](user-guide.md#log-and-diagnostics) for stored-log
retention, clearing and the Report an issue button.

## Updates or profile settings are missing

Use [this fork's releases](https://github.com/Pav-Osmolski/DS4Windows/releases).
Installed users run the matching Setup EXE; portable users replace the complete
packaged app files in their existing folder. Keep profiles, settings and
portable-data. Check both `%APPDATA%\DS4Windows` and the app folder for settings.

### Access denied when saving a profile

Open **Profiles > Settings & profiles** to see the current folder and whether
it is writable. Installed copies using `Program Files` can fail to save when
running normally, even though controller input works. Use **Change location**
to copy settings to AppData, review any destination conflicts, then restart as
requested. Use **Back up all** first; original files are retained. Do not solve
a settings-location problem by disabling Windows folder protection. The saving
and microphone problems in [#80](https://github.com/Pav-Osmolski/DS4Windows/issues/80)
had separate causes.

### Special Actions will not stay enabled, or multiple tap macros fire

Save the action itself, enable its checkbox for **each intended profile**, then
save that profile. Creating or importing an action does not enable it in every
profile. After updating an affected older build, re-enable selections that were
not previously saved; the app cannot reconstruct every lost choice.

The earlier selection-saving problem in
[#33](https://github.com/Pav-Osmolski/DS4Windows/issues/33) and the separate slash
in action names problem in
[#99](https://github.com/Pav-Osmolski/DS4Windows/issues/99#issuecomment-6547849251)
were fixed; use **5.0.58 or later** for names such as **Play/Pause**. There is no
need to recreate actions or edit XML manually just to avoid that delimiter bug.
If a multimedia key still fails after the selection survives reopening, capture
that separately with the affected profile and `Actions.xml`.

For single/double/triple/hold shortcuts, prefer one **Multi-action button** with
the gestures named and enabled individually. Separate actions sharing the same
buttons must also use identical hold/tap timing to share recognition. Enabling
triple tap means double tap waits for the tap interval; that wait is expected.
The separate-action triple-tap overlap in
[#100](https://github.com/Pav-Osmolski/DS4Windows/issues/100#issuecomment-6557362696)
was fixed in **5.0.59**. See [Special Actions](profile-editor.md#special-actions).

### Old update downloads take up space

Use **Settings > Updates & tools > Updates > Update storage**. It shows recognised
stored files and reclaimable space; **Clean up now** previews removal. The
default keeps one previous version per component, with cleanup after a successful
updated start. Current files, profiles/settings and backups are preserved;
unknown or busy files are left alone. Avoid manually deleting active backend
files to reduce storage ([#83](https://github.com/Pav-Osmolski/DS4Windows/issues/83)).

## Game prompts, controller metadata or pressure readings look wrong

- **Xbox One output still shows Xbox 360 icons:** verify the selected output in
  Controller info or a compatible tester, then check the game's own prompt
  options and Steam Input. Artwork alone does not identify the emulated device.
  [#71](https://github.com/Pav-Osmolski/DS4Windows/issues/71) confirmed Xbox One
  detection while the game retained its older artwork.
- **Factory dates differ from the app version:** choose the correct Physical or
  Emulated tab in [Controller info](controller-info.md). Emulated firmware dates
  describe the virtual device. The specific DS4 `20211` timestamp encoding error
  was corrected in **5.0.48**, rather than being a tester limitation
  ([#75](https://github.com/Pav-Osmolski/DS4Windows/issues/75#issuecomment-6384927640)).
  Battery percentages can also be quantised into protocol bands; Xbox USB outputs
  may report wired/no battery instead of the physical percentage.
- **DS3 buttons act digital when mapped to an axis:** use **SXS mode in DsHidMini**,
  reconnect, and map pressure-sensitive buttons to analogue triggers or stick
  directions. Keyboard/button assignments remain on/off, and Switch 2 Pro
  triggers are digital. Pressure support was added in **5.0.56** and confirmed on
  real hardware in [#92](https://github.com/Pav-Osmolski/DS4Windows/issues/92).

For lights left on by an application, use **Lightbar > Indicator LEDs > Off** and
**Apply** or **Reapply indicators**; close any app still changing them. Normal
lightbar mode keeps the profile colour, while **Passthru + Device settings**
allows application control. The colour-ownership fix in
[#28](https://github.com/Pav-Osmolski/DS4Windows/issues/28) and indicator controls
in [#40](https://github.com/Pav-Osmolski/DS4Windows/issues/40) are separate changes.

For clipped readings or inaccessible editor controls, include Windows display
scaling and the approximate window size with a screenshot. **Axis Config > Show
controller readings** provides live readings while tuning; narrow layouts stack
the panels. The scrolling/card-width issues in
[#19](https://github.com/Pav-Osmolski/DS4Windows/issues/19) were addressed in
5.0.21–5.0.22. The displayed report interval is a smoothed observation of physical
input reports, not a measurement of end-to-end game latency
([#59](https://github.com/Pav-Osmolski/DS4Windows/issues/59)).

## Report a reproducible issue

Before reporting, read the relevant sections above and
[search open and closed issues](https://github.com/Pav-Osmolski/DS4Windows/issues?q=is%3Aissue).
Add useful findings to an existing matching report rather than opening a duplicate.
If a closed issue's fix does not work for your setup, explain what is different.

[Open a bug report](https://github.com/Pav-Osmolski/DS4Windows/issues/new?template=bug_report.yml) with:

- App version and full Windows build.
- Controller model, USB/Bluetooth connection and virtual output type.
- Installed or portable setup, and any overlapping mapping software.
- Steps, expected behaviour and observed behaviour.
- Relevant exported log or installer diagnostics, reviewed for personal data.

Use the [feature request form](https://github.com/Pav-Osmolski/DS4Windows/issues/new?template=feature_request.yml)
for proposed improvements, or [Discussions](https://github.com/Pav-Osmolski/DS4Windows/discussions)
for setup questions. If a required detail or log is unavailable, explain why;
there is no need to delete profiles or reinstall drivers just to file a report.

Enable verbose logging only when needed to reproduce a problem, then turn it off.

### Capture VIIPER's own debug log

In **Log**, enable **VIIPER debug logging**. Fully exit DS4Windows and VIIPER
(including its tray icon, if present), then reopen DS4Windows normally. If
VIIPER remains running with its icon hidden, end `viiper.exe` in Task Manager. Do not
launch VIIPER with a separate command. Reproduce the problem and use **Open log
folder** to find `viiper-debug.log`; attach it together with `ds4windows_log.txt`.
The preceding capture is preserved as `viiper-debug-previous.log`. The active
capture is replaced only when another diagnostic VIIPER session starts.

Turn the option off, fully exit both applications and reopen DS4Windows when
finished. Changing the checkbox does not interrupt a running controller session.
The setting is separate from DS4Windows' **Verbose logging** and does not enable
raw packet logging. Installed copies use a direct session launch (Windows may
ask for permission) without changing the startup task. A VIIPER instance already
running at logon must be closed first. Development lab launches continue to
manage their own logging.
For slow profile-editor opening, enable **Settings > Updates & tools > Profile
editor timing logs**. This separate option records `[ProfileEditorTiming]` phases
and is off by default. It separates XAML/template construction, the profile view
model, controller artwork, workspace attachment, initial layout/Loaded callbacks
and the render queue. Opening a settings page for the first time adds a page
construction measurement. Dispatcher timing does not measure when the monitor
actually presents a frame. Turn the option off after collecting the log.
Hardware-specific support is not established by a passing unit test alone.
