DS4Windows + VIIPER: physical-controller DirectInput preview

Extract the entire ZIP into a fresh local folder outside Program Files/AppData.
Exit installed DS4Windows and VIIPER, close games, then run START.cmd. Approve
Windows elevation. USB/IP 0.9.8.1 must already be installed; this preview does
not install drivers. It uses the matching backend and its own lab-data folder.
Installed profiles, updater routing, startup tasks and HidHide are not changed.
On first launch, enable support for your physical controller (DualSense Device
Support for a DualSense), then click Close. Translations use the release Lang
folder layout. The lab deliberately disables automatic HidHide configuration.

1. Connect your physical controller. Create a NEW profile with a normal gamepad
   preset (not a mouse/keyboard preset). Name it DirectInput Test.
2. In Advanced, choose Generic joystick (preview). Start with Legacy games and
   Centred triggers. Save and assign this profile to the controller. Keep Output
   Slots on Dynamic for these tests.
3. Open Windows joy.cpl and select the VIIPER generic joystick, not the physical
   controller. Test both sticks, triggers, all buttons and the direction hat.
   Repeat with Racing layout and Full range / Combined / Buttons triggers.
   Apply/save recreates the joystick. Reopen joy.cpl after changing modes.
4. Check that saving/reopening the profile preserves your choices. Test physical
   unplug/reconnect, changing back to Xbox 360, and returning to Generic joystick.
   Existing output modes should still work. Note any duplicate/stuck devices.
5. EXIT DS4Windows via the tray menu when finished (closing may only minimize).
   The launcher cleans up its imports, stops its backend and prints a report ZIP
   under results. Inspect logs before sharing; keys/profiles are excluded.

Expected first-stage output: six axes, twelve buttons, one direction hat.
No force feedback, virtual rumble, audio or XInput. Only this matched portable
preview enables the option. USB DualSense Windows tests and initial gameplay
in BeamNG.drive and current Steam FlatOut 2 passed; other configurations still
need testing. HidHide is intentionally not
configured here: the physical controller remains visible. Start with joy.cpl;
game device selection/hiding will be tested separately.

Please report controller model/connection, profile layout and trigger mode,
results in joy.cpl, persistence/reconnect behaviour, and any errors. Include
the generated report if useful. This is a feature preview, not a release.
