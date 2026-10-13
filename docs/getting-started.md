# Getting started with DS4Windows 5

[Back to the overview](../README.md)

## What you need

- Windows 10 or Windows 11 **x64**. The VIIPER packages do not support x86 Windows.
- A supported controller and a USB connection or suitable Bluetooth adapter.
- The [Microsoft Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe).
- Administrator permission for driver installation and repair.

The release packages include the .NET runtime, matching VIIPER backend,
USB/IP driver, and optional HidHide and FakerInput installers. You do not need
to find a separate VIIPER download or choose a driver version yourself.
The Visual C++ runtime remains a separate system prerequisite.

## Install

Download only from [this repository's Releases page](https://github.com/Pav-Osmolski/DS4Windows/releases).
Starting with **5.0.14**, new releases use numeric versions and are marked
**Latest**. Check the release notes for limitations and signing status;
this fork currently publishes unsigned builds.

### Moving from another fork

If you are moving from another DS4Windows distribution, including hbashton's
fork, start with a clean set of application files and the matching bundled
backend. Different releases may use different VIIPER and USB/IP versions.
For a routine update of **this fork**, follow
[Update without losing your profiles](#update-without-losing-your-profiles)
instead; you do not need to uninstall a working installation first.

1. **Back up your profiles and settings.** Check both `%APPDATA%\DS4Windows`
   and the old application folder, including any `portable-data` folder.
   Keep the backup until you have verified your controllers and profiles in
   the new installation.
2. **Disable the old copy's startup entry**, then close games, exit every
   DS4Windows copy, and quit VIIPER from its tray icon. Remove old launch
   shortcuts so you do not accidentally start both distributions.
3. **Remove the old application installation.** For an installed copy, use
   its registered uninstaller in Windows' installed-apps list. For a portable
   copy, keep its folder as a backup but stop launching it; extract this fork
   into a fresh folder. Do not overlay a different fork's program files.
4. **Run this fork's x64 Setup EXE** and follow the USB/IP replacement and
   restart instructions below. Portable users must also complete driver
   setup when prompted.
5. Restore only your user profiles and settings if needed. Do not copy old
   executables, DLLs, runtimes, updater files, VIIPER, or driver installers
   into the new installation. Test your controller before enabling startup
   for the new copy.

#### Existing USB/IP drivers

An incompatible old USB/IP package must be removed before the bundled
replacement is installed. **Let Setup perform this check and removal**:
it can reuse an exact, verified supported package, so manually uninstalling
USB/IP is not required for every installation or update.

When replacing a recognized older package, Setup removes it and asks you to
**restart Windows** before installing the replacement. Use **Restart**, then
rerun the same Setup EXE and complete Install / Repair as directed. Complete
any further requested restart before testing controller output. Copying new
application files alone does not replace a system driver.

If Setup cannot safely identify or remove the existing package, use that
package's original uninstaller or its entry in Windows' installed-apps list,
restart Windows, and rerun Setup. If removal still fails, use **Open log** or
**Copy diagnostics** and follow the
[USB/IP recovery guidance](troubleshooting.md#setup-stops-after-the-usbip-replacement-reboot). Do not force-delete driver
files, DriverStore entries, or Setup's replacement-state file to bypass the
checks. If another application uses USB/IP, coordinate its requirements
before removing a shared installation.

Keep working, compatible HidHide, FakerInput, and controller-specific drivers
such as those used by DualShock 3; migrating DS4Windows does not require
removing every controller driver. Use the USB/IP version bundled with this
fork rather than independently upgrading or downgrading it.

### Recommended: all-in-one installer

1. Close games, exit DS4Windows, and quit any running VIIPER from its tray icon.
2. Run the release's **x64 Setup EXE** and follow the prompts.
3. Include **HidHide** to prevent games from seeing both your real controller
   and its emulated counterpart. **FakerInput** is optional for keyboard/mouse output.
4. Restart Windows if setup requests it, then complete any requested repair.

The installer places DS4Windows and VIIPER under
`%ProgramFiles%\DS4Windows`. It includes the required driver payloads;
avoid independently upgrading or downgrading USB/IP for this installation.

### Portable ZIP

1. Extract **all** of the release's `DS4Windows_<version>_x64.zip` (older RCs use `DS4Windows_VIIPER_x64.zip`) into a permanent, writable folder.
   Do not launch from inside the ZIP or copy only `DS4Windows.exe`.
   Keep the `Lang` folder with the application; it contains the translations.
2. Run `DS4Windows.exe` from that folder. On a new PC, complete driver setup
   when prompted; portable does not mean driver-free.
3. DS4Windows starts its bundled VIIPER when needed. A verified matching
   running copy can be reused. If a conflicting copy is reported, quit that
   VIIPER instance before relaunching DS4Windows.

The marked portable package does not create or retarget the installed
DS4Windows/VIIPER startup tasks just by launching.
VIIPER stays beside DS4Windows. Install / Repair restores the matching version
in that folder, downloading it only if needed, without moving your files or
closing DS4Windows. Controller output pauses briefly during an active repair.

## Connect and choose a profile

Connect a supported controller and select it in DS4Windows. Controller-family
options are in **Settings > Controllers > Device Options**; optional Moonlight/Sunshine input
must be enabled separately. Some devices, including DualShock 3, need their
own compatible driver setup.

For **Switch 2 Pro**, use USB or DS4Windows' Bluetooth discovery and
association controls in **Settings → Controllers**. For **Joy-Con 2**, use those Bluetooth
controls. Put the controller into pairing mode and associate it there. See
[Nintendo setup](nintendo-options.md#connect-and-pair) for reconnecting, pairing
and hardware-specific options.

In **Controllers**, use Link/Unlink for Joy-Cons, or enable automatic pairing.
Pairs require an opposite-side controller from the same generation. Choose a
single Joy-Con's upright or sideways holding style manually.

Create or edit a profile and choose the emulated controller the game should
see. **Auto Profiles** can select profiles when a matching game or application
is active. Restart a game after changing emulation if it does not detect the
new controller.

See [Compatibility](controller-compatibility.md) to choose an output and check
hardware/connection limits. [Profile Editor](profile-editor.md) explains its
controls, and [Controller audio](controller-audio.md) covers speakers and microphones.

For native DualSense advanced haptics, select **DualSense** output and enable
controller audio/speaker support. The game must support advanced haptics.
Disable overlapping Steam Input remapping for that game so it can see the
emulated DualSense. Trigger Lab overrides game trigger effects only on the
triggers where it is enabled.

Nintendo HD-rumble translation adapts the source feedback to Nintendo motors;
it is not identical to a physical DualSense and cannot add adaptive-trigger
resistance. Original Joy-Cons do not have Joy-Con 2 optical mouse hardware.

## Update without losing your profiles

Close games, DS4Windows, and VIIPER first. Back up profiles and settings before
updating, especially when moving between versions or installation types.

- **Installed:** run the new release's Setup EXE. Existing profiles are retained.
- **Portable:** extract the whole new ZIP into your existing portable folder,
  replacing the packaged app files. Keep your profiles, settings and
  `portable-data` folder. Do not copy old program or dependency files back over
  the updated ones.

Settings may be in `%APPDATA%\DS4Windows` **or beside the application**, depending
on your configuration. Check both before removing an old portable folder.
Follow the release's update instructions when a matching backend update is
required.

Current fork builds check this repository and use the matching fork DS4Updater
(2.0.9 or newer) for supported installed/portable updates. Configure checks under
**Settings → Updates & tools**, or install manually from
[Pav-Osmolski/DS4Windows Releases](https://github.com/Pav-Osmolski/DS4Windows/releases).

**Older RC4.6.7 builds need one manual upgrade:** they still check hbashton's
upstream releases. Replacing DS4Updater alone does not change that old app's
release source. After installing a current fork build, later update checks use
this fork.

## If something isn't working

- **Double input:** check HidHide is installed and enabled for the physical
  controller, then disable overlapping remapping software for the game.
- **No virtual controller / backend not ready:** follow the displayed startup
  error. Use **Settings > Controllers > Install / Repair VIIPER** when requested, and restart
  only if setup asks. Keep DS4Windows and its bundled VIIPER version together.
- **No native haptics:** confirm the game supports them, the profile emulates
  DualSense, controller audio/speaker support is enabled, and Steam Input is
  not replacing the virtual pad. Restart the game after changing these settings.
- **Game Bar navigation:** enable compatibility under **Profile Editor → Advanced → Output**
  and run DS4Windows elevated. Xbox Game Bar must be installed. See
  [Game Bar setup](integrations.md#xbox-game-bar).

Still stuck? [Open an issue](https://github.com/Pav-Osmolski/DS4Windows/issues) with
your DS4Windows version, physical controller, USB/Bluetooth connection,
emulated controller, and steps to reproduce. Attach the relevant log from your
DS4Windows settings folder's `Logs` directory; check it for personal information
before sharing. If requested, enable verbose logging and reproduce the problem
once, then turn it off again.

For setup failures, use the installer's **Open log** or **Copy diagnostics**
buttons. Detailed setup logs are also under
`%ProgramData%\DS4Windows\Installer`.
