# DualSense Bluetooth haptics backlog: test guide

This candidate addresses backlog behaviour investigated in [upstream issue 124](https://github.com/hbashton/DS4Windows/issues/124). It is a test build, not a claim that the reporter's machine-specific stall is fixed. No VIIPER or USB/IP update is required.

## Get ready

1. Keep your installed 5.0.28 copy available for comparison and back up its profiles/settings.
2. Download the **x64 Portable** artifact linked from the candidate PR's successful CI build. Extract the outer artifact download and then the contained application ZIP into a new folder. Use the complete package; replacing only DS4Windows.exe omits the matching application DLL/helper code.
3. Close the game and DS4Windows, quit VIIPER from its tray icon, then start the test copy. Run one DS4Windows copy at a time. Import a copy of your normal profile through Profiles. Keep the working release and existing drivers for this test.
4. Enable **Settings > Updates & tools > Diagnostics > Verbose logging**. Restart DS4Windows for each comparison run and save its log separately with the build and connection type in the filename. Use **Open log** in that same section to locate the log. A fresh application session makes the cumulative counters easier to interpret.
5. Use the same game, save/location, profile and graphics settings throughout. Choose a game which already gives native DualSense haptics on your PC, keeping its working virtual-controller selection (normally DualSense). Test locally first, without Sunshine/Moonlight. Desktop-audio-to-rumble effects alone do not exercise the same native-haptics source as the report.

## Comparison runs

| Run | Build | Physical connection | Time |
| --- | --- | --- | --- |
| A | Released 5.0.28 | USB | About 5 minutes |
| B | Released 5.0.28 | Bluetooth, USB cable unplugged | 15-20 minutes, or until delay appears |
| C | Candidate portable build | Bluetooth, USB cable unplugged | Repeat B's actions for at least the same duration |
| D | Candidate portable build | USB | About 5 minutes |

If possible, repeat the released Bluetooth run after the candidate run. This helps distinguish an improvement from a transient change in radio conditions. Keep controller distance and normal background workload similar.

During each run:

- Repeat a short, recognisable event such as single shots, impacts or footsteps. Check whether the tactile response follows the event rather than a previous action.
- Alternate 30 seconds of activity with 10 seconds of doing nothing. After activity stops, effects should end normally rather than continue as a queue of delayed impacts.
- Include a pause/menu transition and return to play. Record the approximate time of any delay, missing effect, unexpected sustained vibration or recovery interruption.
- Test your usual controller-speaker or controller-headset audio if enabled. Listen for new pops/dropouts or disagreement between sound and haptics. The candidate retires obsolete rear-channel samples; it does not discard or reorder speaker/control packets to hide a backlog. Audio regression is a test failure even if haptics become fresher.
- Check adaptive triggers, lightbar and microphone mute if your profile/game uses them. After the main run, disconnect/reconnect Bluetooth once and confirm fresh effects return. Keep the profile unchanged during the timing comparison.

## What to send back

Please provide the separate released/test-build logs, game name, whether native haptics worked over USB, controller model/firmware if known, Bluetooth adapter model, and approximate event times. A short video of the screen plus audible controller vibration may help compare repeatable events, but is optional and is not a calibrated latency measurement.

The test build adds these fields to verbose **DualSense Bluetooth combined stream stats** lines:

- `hapticsRetired`: obsolete rear-channel blocks retired during overload, including source publications which waited too long for space.
- `hapticsRecoveries`: consumer catch-up episodes after the age/expiry budget was exceeded.
- `hapticsProducerWaitMaxMs`: longest observed wait for shared-ring space.
- `hapticsLatencyLimitMs=250`: the candidate's software queue age threshold, not an end-to-end or over-the-air latency guarantee.

Keep the existing `hapticsQueueDepth`, `hapticsQueueAgeMaxMs`, `helperCompletionMaxMs` and `pacerError` fields. Queue-age and completion maxima are cumulative: an old peak alone does not show that latency is still rising. Counters may restart when a helper session is replaced. We will review the sequence of log entries, not one isolated maximum. The normal connection ms/Hz display measures report intervals and cannot establish haptic synchronisation.

## Acceptance

A convincing result is reproducible delay in B, followed by a same-length C run without accumulating delayed effects, missing normal effects, repeated transport resets, or audio/control regressions. D should remain consistent with A. If neither released nor candidate Bluetooth runs reproduce delay, report that as a useful compatibility test, not proof of a fix. A one-off recovery that returns to normal is different from constant recovery/dropouts; report both accurately.

The candidate preserves FIFO playback through short interruptions. Beyond 250 ms of queue age it retires complete obsolete rear blocks until within a 50 ms catch-up target, preserving recent authored zero/stop blocks and lifecycle generation boundaries. A source waiting for capacity also has a 250 ms budget. Outstanding asynchronous HID writes which remain incomplete for over one second fault the existing transport owner and use its established cancellation/retirement/recovery path. Already-submitted writes are never overwritten or bypassed. These are initial software budgets to validate on hardware, not claims that Windows I/O or a suspended process can always execute within those times.

Disable verbose logging after testing. Return to 5.0.28 by closing the test copy and VIIPER, then launching the installed copy normally; your original profiles were not replaced.
