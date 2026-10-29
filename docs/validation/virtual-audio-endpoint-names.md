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

## Preview regression correction

The first preview exposed a legacy model-classification bug: matching the token
DS4 anywhere also matched DS4Windows in the new labels, incorrectly classifying
DualSense audio as DS4 and preventing USB haptics source selection. Classification
now prioritizes Sony USB VID/PID information, then uses model-name hints with the
application name removed. Regression cases cover both renamed model families,
labels without model hints, and contradictory display-name/USB identities.

The Windows endpoint probe was extended to require the correct DS4/DualSense
classification and invoke the production USB haptics source finder. DualSense,
Edge and the DualSense audio companion each selected their renamed render
endpoint and exposed four mix channels. This is a routing check without capture
or playback; actual USB and Bluetooth haptics require maintainer listening tests.
A physical-plus-virtual USB enumeration smoke check was attempted but skipped
because the required active endpoint pair was unavailable at that time.

## USB microphone forwarding (preview r3)

The naming-only r2 preview did not forward USB microphone audio into VIIPER.
The r3 preview adds that route for recognized Sony DS4, DualSense and Edge sources
with a Windows recording endpoint. It matches the physical controller's USB
container, excludes USB/IP endpoints, and never falls back to the default mic,
loopback capture, game playback or a virtual-cable render endpoint.

Capture runs on a separate worker only while the profile enables forwarding and
a Windows consumer opens the virtual recording interface. A bounded 100 ms
capture buffer discards stale input on overflow. Mono/stereo capture is converted
to 48 kHz mono in 10 ms blocks, then passed through the existing microphone
processor and output-specific VIIPER packer. Transport writes remain on the
existing writer. Source, stream and route generations reject retired audio;
mute transitions clear buffered/prepared input and the final transport mute
check remains active. Capture failures retry with an initial endpoint grace period
and report an unavailable microphone lane rather than pretending it is armed.

Tests exercise fragmented blocks, clipping, non-finite input, mono/stereo and
16/44.1/48 kHz conversion, incremental resampling, production DS4/DualSense/Edge
packing, software mute, profile disable, virtual recording close, source
replacement and stale-generation rejection. Policy checks cover eligible USB and
Bluetooth controllers, optional audio companions and exclusion of unrelated
headset paths. Adding a USB microphone companion must not activate the native
USB haptics playback bridge for an Xbox/Switch primary output.

The automated checks do not verify physical headset sound quality or prove the
original issue #90 echo is fixed. Maintainer acceptance must check:

1. USB: enable microphone input, apply, open the named DS4Windows microphone in
   Windows Sound/recording software and confirm speech reaches its meter/recording.
2. Toggle microphone forwarding and mute, close/reopen the recording consumer,
   and reconnect the controller; confirm no old audio is replayed.
3. Repeat with Bluetooth, and test native game haptics over both USB and Bluetooth.
4. For NVIDIA issue #90 testing, keep game and microphone recordings separate and
   compare with the physical USB microphone. Do not infer echo from device names.
5. Confirm physical endpoint names/defaults remain unchanged. The virtual mic stays
   visible with forwarding disabled; it is not hidden or removed by that checkbox.

## Final r3 validation record

The application suite passed 7,932 tests with 16 opt-in checks skipped. After
preserving the unsupported-adapter UI guard, 168 focused microphone, capability,
USB haptics and allocation checks passed. The native-feedback allocation check
now uses the suite's existing strict measurement scope to exclude GC allocation-
context repair; its requirement remains exactly zero allocated bytes.

A read-only probe called the production USB capture lookup, but no connected
physical USB controller microphone was available, so that hardware check was
skipped. No microphone recording or speaker playback was performed by the probe.
The r3 preview therefore requires the physical acceptance checks above before
release. It does not claim the issue #90 echo is resolved.

## Reconnect retention (preview r4)

GTA V Enhanced hot-plug testing exposed a separate visibility window. Steam's
logs saw the physical Bluetooth controller before its VIIPER replacement, even
with the game-specific Steam Input mapping disabled. The browser tester appeared
normal while the game exhibited doubled actions. This supports investigating
consumer hot-plug/visibility handling, not declaring a confirmed game or app cause.

The r4 preview adds the default-on reconnect-protection setting described in
[HidHide lifecycle](../hidhide-device-lifecycle.md#reconnect-protection). The
policy is captured at service start. It retains only proven owned persistent
entries after a successful connection disconnects, revalidates late removals,
and includes disconnected controllers in normal service-stop cleanup. Cancelled
connections still roll back additions and preexisting user rules remain untouched.
No input, haptics or microphone packet paths are changed by this follow-up.

The full application suite passed 7,942 tests with 16 opt-in checks skipped;
156 focused HidHide/settings/language checks passed. Tests cover enabled and
disabled policy, reconnect reuse, user-rule exclusion, late-write reassertion,
cancelled connection rollback, failed-Stop resumption, full-stop release, and
missing/invalid/true/false preference serialization. New UI text is present in
all 24 language packs. No live HidHide rules or installed settings were changed.

Maintainer acceptance: restart into r4 with the option enabled, launch GTA only
after the virtual controller is ready, then disconnect/reconnect Bluetooth while
the game remains running. Compare Steam's physical/virtual discovery and input
behavior with r3. Verify Stop releases process-owned rules, native access returns,
and unrelated user-created hiding entries remain. Do not claim a GTA fix until
that hardware/game comparison completes. R3 microphone USB/BT listening passed;
the session exercised rumble/trigger commands, while audio-waveform haptics were
not observed in the supplied counters.
