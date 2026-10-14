DS4Windows / VIIPER experimental DirectInput lab

This is an isolated synthetic joystick test, not a DS4Windows update.
It does not yet let you play games using your physical controller.

1. Close DS4Windows, VIIPER and games. Leave your installed files in place.
2. Extract this entire ZIP to a NEW writable folder (not Program Files).
3. Double-click START.cmd and accept the Windows administrator prompt.
4. Wait for the automatic test to finish. Do not open other controller apps
   during the test. You do not need to move or connect a physical controller.
5. Send back the *-report.zip file shown at the end, inside the results folder.

Requires 64-bit Windows and the already-installed USB/IP 0.9.8.1 runtime.
No Go, .NET SDK or C++ tools are required on the test machine.

The launcher verifies the bundled files, uses private localhost ports and a
separate backend/key/configuration, and refuses existing USB/IP imports.
It creates one temporary joystick at a time and tests two axis layouts with
centred, full-range, combined and button triggers. Both 32-bit and 64-bit
DirectInput and WinMM are checked, including disconnect neutral/reconnect.
The first input stream opens only after Windows has enumerated the joystick.
Both native probes then keep their consumers connected throughout each plan,
so a sample is not taken from a newly opened consumer's initial zero state.
Backend debug diagnostics record published input and presentation counters.
Only a port matching the lab's private server and allocated device is detached.
Installed apps, drivers, startup tasks and controller hiding are not changed.

On PASS, all synthetic readings matched and the imports were removed.
On FAIL/INCOMPLETE, send the report too: a missing API device or differing axis
assignment is useful evidence. Do not replace your released VIIPER backend
with this executable. Game compatibility and force feedback are not claimed.

If cleanup reports a remaining lab import, close the lab and restart Windows
before restarting DS4Windows. Do not detach another application's ports.
If you close the test window or interrupt it, automatic cleanup may not finish;
restart Windows before continuing. Use a normal completion whenever possible.

The report contains API readings, OS version, source revisions and backend logs.
Its explicit file allowlist excludes the API password; lab.key.txt stays local.
Inspect the report before sharing it if you prefer.

Developer-only non-mutating check:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Run-Lab.ps1 -ValidateOnly
