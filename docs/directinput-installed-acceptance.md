# Generic joystick: remaining acceptance tests

Use this checklist once a matched normal candidate has the approved twelve-button
USB identity and verified backend pins. The current portable preview bypasses
automatic HidHide and cannot validate these installation tests. Do not replace
the released backend by hand to create a candidate.

## Before testing

Back up profiles/settings and record the DS4Windows, VIIPER, USB/IP and HidHide
versions, build hashes, controller model/connection, Windows version and Steam
Input state. Keep a known-good installer available. Use DS4Windows' built-in
HidHide controls and record whether other apps already own hiding rules.
One physical controller is sufficient for these checks; it does not establish
multi-controller binding stability.

## Normal candidate installation and upgrade

1. On a clean test installation, install the matched candidate using its normal
   setup. Confirm normal startup, backend verification and twelve-button joystick
   creation. Record any reboot request and resume through the normal setup path.
2. Separately upgrade an existing installation containing Xbox/Sony profiles.
   Check profile names, selected outputs, mappings and settings before selecting
   joystick output explicitly. Existing profiles must not switch to joystick.
3. Save a joystick profile, restart DS4Windows and reopen it. Check layout,
   trigger mode, inversion, thresholds and the same physical controller identity.
   Switch to Xbox 360, DualShock 4 and back; confirm the selected output works
   without stale or duplicate virtual devices.
4. Run installer repair, relaunch and verify the same settings and output.
   Test unsupported/malformed joystick settings using a disposable profile copy:
   refuse activation clearly, preserve the original file on failed save, and
   avoid silent output substitution.

## Built-in HidHide and cleanup

1. Enable physical-controller hiding through DS4Windows. In a game or an
   independent controller viewer, verify one virtual joystick is available and
   the physical controller does not cause duplicate input. Confirm DS4Windows
   itself continues reading it. Record the viewer/application allow-list state.
2. Exercise unplug/reconnect, Stop/Start, output switching and full exit/relaunch.
   Verify no stuck axes/buttons, orphan virtual imports or repeated prompts.
3. Disable managed hiding and check physical visibility returns according to the
   existing ownership policy. Pre-existing user/foreign hiding rules must remain
   intact; do not expect DS4Windows to erase rules it does not own.
4. If Bluetooth is available, repeat connection/cleanup checks over Bluetooth.
   Record unavailable combinations as untested rather than passed.

## Saved game bindings: can be checked with the existing preview

In the already-tested BeamNG.drive or FlatOut 2 configuration, save bindings,
exit the game and DS4Windows, then restart DS4Windows with the same controller,
profile and USB port. Reopen the game and drive without rebinding. Repeat after
one unplug/reconnect. Record which axes/buttons retained their bindings and any
changed device identity. Do not use this as evidence for two-controller
persistence; that scenario has a known GUID reassignment limitation.

## Result to retain

Keep the candidate hashes, setup/app/backend logs, pass/fail per step, hiding
ownership before/after and any game-binding changes. A synthetic test checks
policy and error paths; actual driver installation and cloaking require this
Windows acceptance run. See the [release checklist](directinput-release-checklist.md)
and [identity audit](directinput-identity-audit.md) for remaining release gates.
