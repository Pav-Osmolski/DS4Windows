# DualSense native feedback and Log tools - 18 October 2026

[Issue #47](https://github.com/Pav-Osmolski/DS4Windows/issues/47), reported by
@Jobima1st, includes a USB log with native command admission/queue delays above
one second. Its Bluetooth log ends before a sustained gameplay statistics
sample. The related upstream [issue #119](https://github.com/hbashton/DS4Windows/issues/119)
reports weakness without comparable queue delays. These are separate symptoms;
the logs do not measure actuator strength.

Implementation: [PR #50](https://github.com/Pav-Osmolski/DS4Windows/pull/50).

## Scope in 5.0.32

- Physical-device lifetime counters cover native command admission retries,
  pending/high-water depth, current head age, maximum queue/owner/completion
  time, rumble/trigger patterns, explicit motor-stop receipt revisions and the
  last native validity flags, motor values and actuator-power byte.
- The compact summary includes dispatch wait/depth/age and repeat counters.
  It reaches the copyable verbose GUI log every 30 seconds while input is
  active, including during speaker streaming. Formatting and log publication
  run on a background worker, outside transport admission locks.
- USB completion means the physical HID write returned success. Bluetooth
  completion means the existing transport accepted ownership; it is not a
  radio-completion or actuator acknowledgement. Local profile overrides can
  affect the output. Stop counters refer to native motor-stop commands, not
  every trigger-only report containing zero motor bytes.
- The USB repeat extension recognizes a narrow motor/trigger state shape with
  actuator-power validity, and suppresses only a consecutive byte-identical,
  unclaimed pending copy. It preserves the original receipt and age. Claimed
  heads, ownership changes, media, unknown payloads, distinct transitions and
  alternating rumble/trigger commands remain ordered. Bluetooth routing,
  gain and command validity interpretation are unchanged.
- The Log page adds verbose control, folder access, an issue-form link,
  persisted retention and confirmed archive cleanup. The active session file
  and visible list survive archive cleanup; unrelated files, subfolders and
  links are outside the cleanup scope. Archives rotate daily/at 10 MB and
  remain limited to 20 files in addition to the age preference.

## Automated checks

Hardware-free tests cover consecutive 10,000-report bursts, terminal stops,
alternating command preservation, claim/profile/binding/media boundaries,
unknown commands, Bluetooth exclusion, queue retries and original timestamps,
explicit stop detection, deterministic queue/write timings, and allocation-free
recording after warmup. A production logging-path test receives the compact
summary through the GUI event during simulated active speaker streaming and
checks the 30-second scheduling limit.

Temporary-file tests cover age and count retention, locked-file failure/retry,
active-session and unrelated-file preservation, XML defaults/round trips and
real NLog rotation/continued writing. Production Log-page markup is measured
and rendered in Light/Dark themes at 600 and 900-pixel content widths, with
every new control reachable and at least 200 pixels left for the virtualized
log list in a 480-pixel-high content viewport.

These checks do not establish a fix for the reporter's game or Bluetooth
adapter. No hardware gain or opaque-command recovery policy is inferred from
the synthetic tests. Issue #47 remains open for reporter validation.

## Reporter test

1. Update to 5.0.32. Enable **Log > Verbose logging** and restart DS4Windows.
2. With the same profile and virtual DualSense output, play GTA V Enhanced
   for 1-2 minutes. Include drifting and releasing all controls. Note roughly
   when delayed, weak or skipping vibration happens.
3. Use **Log > Copy Text** and paste the full log, labelled **USB**. Repeat
   over Bluetooth and label that log **Bluetooth**. Include successful results.
4. Attach the affected `full ps5 DS.xml` profile. If startup has left the
   1,000-entry view, attach the saved file using **Open log folder** too.
5. Turn verbose logging off after testing. If needed, compare one run with
   controller-speaker streaming disabled, leaving other settings unchanged.

Use the [Log guide](../user-guide.md#log-and-diagnostics) and
[field explanations](../troubleshooting.md#delayed-or-weak-dualsense-vibration).
