TESTING ONLY - ISSUE #98 DUALSENSE HAPTICS DIAGNOSTICS

This preview diagnoses missing feedback. It does not claim to fix #98.
VIIPER remains 0.1.17 and USB/IP remains 0.9.8.1. No reinstall is needed.

1. Extract the entire ZIP to a NEW writable folder. Keep your normal installation.
2. Fully exit your normal DS4Windows, including its tray icon.
3. Double-click START-ISSUE98-DIAGNOSTICS.cmd in the extracted folder.
4. Enable Verbose logging and VIIPER debug logging on the Log page. If either
   was off, fully exit the preview and repeat step 3 so both take effect.
5. Use your affected profile with DualSense emulation. Connect only the affected
   DualSense over Bluetooth. Wait until the controller is Ready.
6. Close the game. In the diagnostic window, choose Run controlled haptics test.
   Expect two gentle pulses, first left then right. Answer the physical-result
   prompt. A ZIP is saved, including failures. Do not reinstall if it fails.
7. Launch The Witcher 3, reproduce the same sword/Sign/combat actions for at
   least 90 seconds. While the game remains open, choose Save game-test logs.
8. Share BOTH ZIPs from this preview's Diagnostics folder. Tell us whether the
   game vibrated, which actions you tested, your Steam Input setting and whether
   either Bluetooth adapter used a hub or the same USB socket.

Do not change drivers, audio defaults or USB power settings during this first
comparison. The launcher only reads adapter/driver/USB-parent and power metadata.
Missing permissions are recorded; do not run as administrator just for inventory.
The test targets only the uniquely matched virtual DualSense render endpoint.
It never falls back to desktop speakers or the physical USB audio endpoint.
Endpoint mute/volume are reported, not changed. It records no microphone or game
audio. ZIPs contain logs, your active profile, audio endpoint/session metadata
and adapter information. Review before sharing. Nothing is uploaded automatically.

Timing numbers are submission-to-observation times, not measured actuator latency.
Startup and streaming measurements are separate; streaming includes silence.
To leave the preview, exit it completely before reopening your normal version.
