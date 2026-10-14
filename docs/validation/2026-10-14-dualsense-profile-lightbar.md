# DualSense profile lightbar ownership

Baseline: main `58b34a585b1e82513fb4fe315c2dd669b14562e9`.

Issue #28 reports an application replacing the physical lightbar colour when
emulating DualSense with the profile's output mode set to Normal. Scalar
feedback already checks LightbarMode.Passthru, but native USB feedback and
combined Bluetooth feedback intentionally retain whole native command streams.
Their compositor previously omitted the profile's lightbar mode, so native
RGB and LED ownership could persist after the application stopped sending.

The immutable physical output snapshot now carries profile RGB ownership.
CheckProfileOptions publishes it on profile activation, and DS4LightBar's
input-side routine tracks live editor mode changes, including Pass-through
frames that do not publish a local colour. Unchanged mode publications are
suppressed by the existing mailbox and do not wake the physical writer.

The common native report compositor applies Normal-mode RGB/setup fields to
the disposable physical report. Both USB commands and Bluetooth native/media
templates use this boundary. Raw game commands and cached underlays remain
intact; returning to Pass-through can use their current colour. Adaptive
triggers, audio, rumble and player LED fields keep their existing ownership.
Explicit DSX presentation overrides retain their established priority, and
DSX colour release restores the profile rather than a held native game colour
when the profile is in Normal mode.

Regression: 8 of the initial 10 policy cases fail without the compositor fix;
both Pass-through cases pass. With the fix, 14 new cases pass alongside the
existing physical ownership, native LED lease, DSX and command-dispatch tests
(209 focused tests). Coverage includes USB and Bluetooth state offsets,
untouched trigger/audio/rumble/player bytes, native LED-release commands, held
game ownership, profile-mode restoration without a new native packet, the
actual USB physical FIFO, and the live profile lightbar routine.

No physical controller or WebHID browser test was available for this work.
The issue reporter should validate Normal and Pass-through on their hardware.
DirectInput work remains separate.

## Release qualification follow-up

The first post-merge main CI run (`38579920002`) aborted in
DSXUdpListenerTests.ConcurrentStartStopAndDisposeFinishWithoutLockInversion.
The unhandled NullReferenceException originated inside UdpClient.Receive,
which reads its mutable socket field after its disposed check; concurrent
Dispose can clear that field. The existing parser/handler catches do not
cover this framework receive race. This is separate from lightbar ownership.

The listener now captures the underlying Socket before publishing its
session and receives through that exact reference. Its existing disposal
and socket-exception handling remains responsible for shutdown and unexpected
listener failure. The 65,536-byte receive capacity and per-datagram copies
match the previous UdpClient behaviour, preserving packet boundaries and
the existing 16,384-byte parser limit. Added real IPv4/IPv6 loopback tests
send empty and oversized datagrams and verify a subsequent status response.
All 140 DSX listener/parser tests pass locally.

The first draft run (`38579929509`) independently failed the existing,
unchanged ControllerFeedbackRuntimeTests allocation assertion (2,056 bytes).
The same assertion previously failed intermittently during 5.0.22 qualification.
Neither its assertion nor the measured feedback runtime is changed or skipped;
the final complete run must pass it before publication.
