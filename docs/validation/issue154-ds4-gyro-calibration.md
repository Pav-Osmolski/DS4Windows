# Issue 154: compatible DS4 gyro calibration preview

This preview investigates [upstream issue 154](https://github.com/hbashton/DS4Windows/issues/154), reported by [Raine-07](https://github.com/Raine-07). QRD Spark N7 is inverted over Bluetooth, while Leadjoy Saber Plus works with the same profile. Both identify as Sony 054C:09CC. PadForge and Kitsune reportedly give correct gyro direction.

## Scope

Only the physical DS4 calibration acceptance path changes. Use a positive gyro sensitivity denominator based on the distances from the bias to both calibration endpoints. Reject implausible coefficients (bias magnitude over 1024 or gain outside 0.5 through 1.5), nonpositive gyro speed, equal endpoints, and zero/negative invalid ranges. Reject failed feature reads, wrong report IDs and bad Bluetooth CRCs with bounded retries. Preserve previously accepted calibration on rejection; a new device retains its uncalibrated nominal input path. DualSense/Edge keep their separate existing calibration reader and policy. No haptics, audio, lighting or virtual-controller transport changes.

This follows the general approach observed in SDL's PS4 driver, not a QRD-specific inversion table. No PadForge code is copied. It is not confirmed that QRD supplies reversed calibration endpoints until a physical capture establishes that.

## Diagnostics

A connection-time `DS4 motion calibration` log entry records USB/BT layout, read/CRC outcome, gyro speed, per-axis bias/endpoints/gain/validity, and raw feature bytes. Calibration reports contain sensor coefficients, not serial numbers. The rest of the app log can contain device identifiers; review before sharing. There is no continuous high-rate motion logging. No verbose setting is required for this entry.

## Synthetic verification

`Ds4CompatibleGyroCalibrationTests` exercises every gyro axis and both movement signs across USB and Bluetooth layouts, ordinary and reversed endpoints, valid nonzero bias, invalid ranges/bias/speed, short/missing data, accepted-coefficient preservation, failed reads, wrong report IDs and corrupt CRC retries. Existing DualSense Bluetooth feature-reader and canonical virtual motion tests remain part of the focused gate. Full CI and portable package verification are required before publication.

## Physical comparison requested

1. Fully exit the installed DS4Windows and other controller mappers. Extract the preview into a new writable folder, not over the installed app.
2. Use a copy of the same affected profile: Emulated controller **DualShock 4**, Gyro output **Passthru**. Keep the Dolphin/Steam gyro configuration unchanged.
3. Connect only QRD Spark N7 over Bluetooth. Test left/right, up/down and tilt directions. Also confirm the lightbar still responds.
4. Export the app log from this session, even if fixed. Test Leadjoy separately with the same profile to check it remains correct, and export that log too.
5. Say which directions work/invert for each controller. Do not run PadForge or Kitsune simultaneously. No driver replacement or reinstall is needed.

Hardware confirmation is pending. Do not close the issue or merge to main based on synthetic results alone.
