# Virtual controller audio names

Validation date: 2026-10-29.

The endpoint-label worker identifies devices by their verified USB/IP ancestor
port and the owning input-controller slot. It never chooses targets from a Sony
name or VID/PID alone. It edits the audio endpoint's user-facing DeviceDesc
through Windows audio policy; Windows generates FriendlyName with its adapter
suffix. USB descriptors, HID identities, endpoint IDs, audio defaults, device
visibility and volume are not edited.

The naming worker runs outside input and UI threads and retries enumeration for
up to 30 seconds after connect/rebind/recovery. Retirement fences property writes
before a port can be detached/reused without waiting for slow PnP enumeration.
Naming errors are informational and do not fail controller startup. Outputs
without audio endpoints are excluded.

## Automated checks

The focused unit tests cover unique controller/direction names, physical and
other-controller exclusion, invalid ports, retirement between writes,
idempotence, failed writes, and recognition of the new playback labels by the
Bluetooth game-audio policy.

## Windows endpoint integration

A local probe created real VIIPER virtual USB devices on Windows 11, with a
standard-user token (Administrator: False), using the production naming worker.
Both render and capture labels were observed through Core Audio for:

- DualSense combined duplex v5
- DualSense Edge combined duplex v5
- DualShock 4 duplex v3
- DualShock 4 audio-only duplex v3
- DualSense audio-only duplex v5

For example: DS4Windows Controller 2 Microphone (2- Wireless Controller).
The probe used slot 2 deliberately, separate from the first-slot unit examples.
Controller render-endpoint recognition continued working after renaming. The
probe removed its devices and stopped its temporary server. The physical USB
DualSense retained its original microphone and speaker names.

The Windows 10/11 audio-policy interop includes the FX-store selector in
SetPropertyValue. The protected synthesized FriendlyName is not writable by a
standard user; the editable DeviceDesc label is. The final implementation was
verified with the latter. It does not request elevation or write registry ACLs.

This verifies endpoint presentation, not physical headset recording quality,
DS4 Bluetooth microphone decoding, NVIDIA capture, or the echo in issue #90.
Windows 10 was not available for the live endpoint check. Applications caching
their audio-device list may need to reopen it to display updated names.
