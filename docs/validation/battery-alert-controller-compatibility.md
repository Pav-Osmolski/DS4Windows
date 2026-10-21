# Battery alert controller compatibility validation

This records the automated coverage and physical checks for the battery alerts introduced in [PR #65](https://github.com/Pav-Osmolski/DS4Windows/pull/65). Automated tests exercise battery interpretation, delivery policy and output packets without requiring controllers. They cannot establish that a physical speaker or motor produces the expected sound or vibration.

## What was checked

| Controller family | Automated evidence | Physical confirmation |
| --- | --- | --- |
| DualShock 3 | Simulated USB/Bluetooth battery metadata, low-band alerts and charging exclusions; real DS3 packet composition for an alert pulse and restoration of newer game feedback | Not tested with hardware |
| DualShock 4 | Simulated USB/Bluetooth percentage readings, automatic alert delivery and reconnect suppression; existing DS4 feedback and audio tests | Battery alerts not tested with hardware |
| DualSense / Edge | Simulated USB/Bluetooth percentage readings and alert delivery; exact endpoint audio format/channel tests and USB/Bluetooth report routing tests | Maintainer heard DualSense speaker battery alerts over USB and Bluetooth on 21 October 2026. Edge not separately tested |
| Original Joy-Con | Simulated left/right low-band metadata; real joined-controller rumble encoding sends the pulse and subsequent neutral state to both halves, with either half as owner | Not tested with hardware |
| Original Switch Pro | Simulated USB/Bluetooth low-band metadata and automatic service delivery; existing Nintendo output tests | Not tested with hardware |
| Switch 2 Pro | Real input-frame mapping of low/high battery voltage for USB/Bluetooth; alert pulses reach the owned USB packets and Bluetooth feedback writer, then release to neutral | Not tested with hardware |
| Joy-Con 2 | Real left/right input mapping and joined-pair battery handling: the lower half determines the combined level; existing runtime/output tests | Not tested with hardware |
| Unknown battery / no-battery clones | Reject unreliable readings; do not infer low/full alerts from unknown values | No hardware claim |

Speaker playback is available only on supported DS4/DualSense routes. The other families use the selected PC audio device and/or supported vibration. Tests verify that unsupported controller-speaker requests fail with a helpful message before synthesis or playback.

## A defect found by the tests

Original Nintendo controllers and DS3 expose coarse battery bands. A low report can be represented internally as 25%, which exceeded the default 20% alert threshold. New tests reproduced six failures, including an automatic service test with several controller families connected together.

The fix uses the reported **low** band for band-only controllers, including the delivery-time recheck. Percentage thresholds and recovery hysteresis still apply to percentage-reporting controllers. Spoken coarse readings say low, medium or high rather than presenting an approximate percentage as precise.

## Behaviour covered

- Stable low readings generate one automatic alert; charging and unreliable readings do not.
- Recovery and cooldown allow a later low event, while reconnects and service restarts suppress duplicate alerts for the same controller identity.
- An eight-device service test covers six eligible controllers, a no-battery clone and a charging controller together.
- Alert vibration restores newer game feedback and cannot cancel an editor preview that took ownership afterward.
- Full-charge notifications require explicit charging completion; high voltage or a 100% reading alone is insufficient.
- Controller speaker audio leaves the DualSense haptics channels silent and uses the matching physical USB endpoint or Bluetooth route.

The maintainer's successful playback log corroborates physical USB endpoint playback followed by Bluetooth delivery through the matching virtual endpoint. These checks establish audible DualSense playback on both transports; they do not certify every controller, firmware or Windows audio configuration.

## Reproducing the automated checks

Run the normal Release/x64 `DS4WindowsTests` suite. The added cases are in `BatteryControllerCompatibilityTests`, `Ds3FeedbackBacklogAuditTests`, `LegacyJoyConHdRumbleTests`, `Switch2RuntimeInputDeviceTests`, `Switch2BluetoothFeedbackLifetimeTests` and `Switch2ProUsbOwnedFeedbackActivationLifetimeTests`.

Hardware-dependent opt-in tests remain separate. Physical confirmation is still needed for other controller families, automatic battery transitions and audible/tactile output on their hardware.
