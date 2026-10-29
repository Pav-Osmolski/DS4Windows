# Issue 95: simulated second-controller checks

Validation date: 2026-10-29. Report: [second DualSense connection attempts](https://github.com/Pav-Osmolski/DS4Windows/issues/95).

These checks use fake HID/native I/O and the real production worker and audio
manager code. They do not create a Windows controller, open WASAPI capture,
connect to VIIPER, or touch the maintainer's running r5 session.

## New simulations

- Start a second Bluetooth DualSense worker generation while the first device's
  output owner is blocked. The second processes output, stops, and starts a new
  generation while the first remains blocked. Hardware writes and finalization
  are replaced by test hooks; all workers are joined on exit.
- Stall one controller's audio-source startup and exercise startup/retry,
  retirement and restart in the other slot. Repeat with the slot roles reversed.
  The real per-slot manager/retry logic runs; a missing source prevents audio or
  hardware activation. Bounded admission waits fail rather than hanging behind
  a blocked first controller.
- Confirm the supported diagnostic comparison: Bluetooth plus virtual DualShock 4
  with speaker playback and Audio Haptics disabled does not request the silent
  DualSense carrier. Enabling Audio Haptics does request it, so that option must
  also be disabled for the comparison. Microphone forwarding should be disabled
  separately as instructed in the diagnostic procedure.

## Results and limitations

All four new cases passed. The expanded relevant set passed 119 tests, with
three opt-in application-audio capture checks skipped. This covers physical
worker ownership, audio-start/stop isolation, command disconnect, virtual slot
removal, HidHide lifecycle and audio policies. No production change or new
preview was needed: the installed r5 application remains unchanged.

The expanded run also exposed a 24-byte allocation-counter artifact in an
existing writer test. Disabling tiered JIT made that assertion pass. The test
now enters optimized code before measuring and uses the suite's strict no-GC
counter scope, retaining its exact zero-byte requirement and adding a positive
allocation control. Both controls pass with normal runtime settings. Production
JIT settings and writer implementation are unchanged.

These results do not reproduce the reporter's Bluetooth failure or rule out a
remaining application problem. They cannot model over-the-air pairing, adapter
firmware, Windows Bluetooth state, radio congestion or two genuine controller
connections. The supplied logs only show the second controller after its
successful connection. The failed connection stage is still unknown.

The reporter should perform one supported comparison with Steam closed, virtual
DualShock 4, and speaker/microphone/Audio Haptics disabled, and observe whether
Windows reports the second controller connected during a failed attempt. That
is additional evidence to locate the failure, not an instruction to replace
hardware or a claim that the carrier causes the issue.
