# DualSense native-command pressure test

Candidate for [issue #47](https://github.com/Pav-Osmolski/DS4Windows/issues/47).
The reporter's 5.0.33 logs show physical-command queue waits up to about 277 ms
over Bluetooth and 252 ms over USB. Disabling optional speaker streaming did
not resolve their symptoms. The summaries cannot establish that the queued
reports are byte-identical, and their maxima do not timestamp every symptom.

## Quick test

1. Close the game and DS4Windows. Extract the complete candidate portable ZIP
   into a separate folder and run its `DS4Windows.exe`. Check that the first log
   line says **5.0.33-issue47-native-pressure1** so you test the correct build.
   Keep your normal app folder and profiles as a backup. If the candidate starts
   with fresh settings, import a copy of the profile used for the previous test.
2. Keep **Emulated Controller: DualSense**, game feedback enabled, Audio Haptics
   off and Trigger Lab off. For the first comparison, leave **Stream audio to
   controller** off, matching the reporter's latest test. Enable **Log > Verbose
   logging**, then restart this candidate.
3. Connect by USB and play GTA V Enhanced for 1-2 minutes. Drive, accelerate,
   brake and drift, then release the controls. Check that vibration follows
   the action and stops promptly, and that adaptive trigger effects still work.
   Note the approximate time if feedback sticks, skips or feels delayed.
4. Use **Log > Copy Text** and save the whole log labelled **candidate USB**.
   Close the game, reconnect by Bluetooth, and repeat with the same profile and
   game settings. Save that full log labelled **candidate Bluetooth**.
5. If you normally use controller speaker/headset audio, enable streaming again
   and repeat briefly to check sound, rumble and triggers together. Turn verbose
   logging off when finished.

Compare the same driving sequence with released 5.0.33 if possible. If neither
build reproduces the problem, report that as a compatibility check. The
DualSense Tester webpage can check basic rumble and triggers, but GTA V Enhanced
is needed to check the reported command-load problem. No driver update is
required for this candidate.

If you do not own GTA V Enhanced, use the familiar DEATHLOOP intro and the
profile editor's **Test Heavy** and **Test Light** on both connections as a
quick regression check. That checks existing feedback still works; Jobima1st's
GTA comparison is still needed to assess their reported delay under load.

## What this candidate changes

The earlier reduction only shared identical unclaimed reports in VIIPER's
small dispatch queue. This candidate also shares identical state updates still
waiting behind the physical-output head, and recognizes documented haptic and
trigger power-reduction settings alongside compatible/improved rumble, known
trigger state and LEDs on both USB and Bluetooth.

The first occurrence keeps its receipt and original queue timestamp. Already
claimed or retrying heads, different prepared profile/Trigger Lab state,
stream/profile/binding/media boundaries and local-output generations prevent
sharing. Distinct changes, A/B/A transitions and final stops retain FIFO order.
Unknown, audio-routing, reset/calibration and media-bearing commands are not
folded. This does not raise gain, increase PCM age budgets, enlarge the FIFO or
drop arbitrary old commands. Genuinely distinct overload can still queue.

## Reading the results

`nativePhysicalPendingRepeats` measures sharing in the physical queue;
`nativeRepeatsSuppressed` measures sharing in the earlier dispatch queue. Compare
their changes with `nativePhysicalPending`, `nativePhysicalHighWater`, queue
age, admission retries and symptom times. A nonzero repeat counter alone does
not prove the perceived delay is fixed. Maxima are cumulative, so inspect a
sequence of snapshots. Physical completion means a USB HID write or Bluetooth
owner handoff, not measured radio/actuator timing.

Automated tests exercise 10,000 repeat admissions, improved/compatible rumble,
power-value bounds, real VIIPER-to-physical admission, full-queue retry, original
receipt/age, ownership barriers, unknown extensions and final-stop delivery.
They use synthetic I/O; hardware acceptance requires the comparison above.

## Additional checks on the updated branch

These changes follow the public `5.0.33-issue47-native-pressure1` ZIP and are not
included in that download. They will accompany the next candidate.

- Open **Profile Editor > Advanced > Controller polling**. DualSense/Edge and
  Nintendo controllers should explain that DS4Windows does not offer an
  adjustable Bluetooth input rate for their backends. No ineffective DS4 rate
  dropdown should be shown for those devices.
- A DS4-compatible controller retains its saved interval. Unrecognized identities
  explain that the requested rate is unverified and may be ignored; recognized
  Sony IDs are not presented as proof of genuine hardware or an accepted rate.
- For USB, check the advertised input and output intervals independently. The
  lookup uses the physical HID interface, negotiated speed and active pipes,
  including through hubs. Failure must show unknown, without assuming a rate.
  Reopen the editor after changing connection. This feature neither overclocks
  USB nor applies the Bluetooth selection to USB.
- A read-only check of Pawel's USB DualSense on 2026-10-19 resolved its HID input
  and output endpoints at 250 Hz (4 ms) each, separate from its audio interfaces.
  This checks discovery on that device, not every clone or host-controller path.

The branch also preserves native Bluetooth disable-audio/rumble-decay mode on
following silent carriers after a trigger-only command. Automated tests cover
this transition and the later explicit release; game feel still needs hardware
confirmation. Existing test-build logs remain useful and no new profile
comparison is a prerequisite for the investigation.
