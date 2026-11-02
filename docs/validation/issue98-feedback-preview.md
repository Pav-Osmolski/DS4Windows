# Issue #98 feedback diagnostic preview

This is a diagnostic comparison, not a claimed fix for missing haptics.

Build from the issue branch with `Issue98DiagnosticsPreview=true` and a distinct
preview informational version. This includes the three files in `utils/issue98`
at the package root. Ordinary builds omit the launcher. The companion window
opens only when `DS4WINDOWS_ISSUE98_DIAGNOSTICS=1` is explicitly supplied.

The launcher refuses to start a second DS4Windows process. Its Bluetooth,
driver-version, USB-parent and power inventory queries are read-only; incomplete
inventory is recorded rather than requesting elevation. It does not repair,
rebind, disable or restart any device.

The controlled test requires exactly one physical DualSense/Edge over Bluetooth
with a ready matching virtual DualSense/Edge output. It uses the virtual
output's USB/IP port and controller identity to select exactly one active render
endpoint; it refuses missing or ambiguous matches and non-four-channel formats.
It never falls back to a default or physical render endpoint. The output
generation and slot ownership are rechecked during playback.

The generated two-second, four-channel float signal contains two rear-channel
120 Hz pulses capped at 0.15 amplitude, with fades and silent front channels.
Cancellation, errors and the four-second playback deadline stop the output.
The provider is finite even if the diagnostic window cannot update. Windows
playback errors and incomplete signal consumption are reported, not treated as
successful physical feedback. The user reports whether both pulses were felt.

The companion records endpoint state, format, mute/volume, default render roles,
render-session PID/name/state, timestamps and writer-phase observations. It
copies the two existing logs and the active profile into a unique local ZIP;
copy failures are listed. No audio recording or automatic upload takes place.
Users should review metadata/profile contents before sharing. A second capture
button preserves the game-session logs without sending another test signal.

Verbose stream formatting continues to be deferred while media is active to
avoid disturbing timing-sensitive playback. The companion explicitly samples
the helper counters before and after its controlled test. Startup and streaming
timing are submission-to-observation measurements, not actuator acknowledgement;
streaming includes silence. The private helper protocol is incremented to 18 so
an older helper cannot silently misinterpret the expanded acknowledgement.

## Automated verification

- `DualSenseBluetoothRealtimeWriterOwnershipTests`: late observation of a
  signalled startup completion stays in the startup bucket after streaming
  begins; synchronous/asynchronous counts remain coherent; pending ownership,
  cancellation and zero-allocation checks remain covered.
- `DualSenseBluetoothWriterDiagnosticsProtocolTests`: existing and appended
  metrics are decoded at the correct offsets without starting a helper.
- `DualSenseBluetoothNativeOrderingTests`: actual helper acknowledgements
  publish streaming metrics while preserving native/media ordering contracts.
- `DualSenseBluetoothAudioHelperProtocolTests`: protocol-18 handshake rejects
  older protocol 17; framed receiver tests cover the extended exact length and
  zero-allocation processing.
- `Issue98RearPulseProviderTests`: complete finite output, bounded amplitude,
  silent front channels, both rear channels and unaligned reads/buffer bounds.

The full application suite passed 8,003 tests with 16 opt-in skips during
preparation. These tests do not establish physical vibration on the reporter's
machine. Keep both a working controlled-test ZIP and a failing game ZIP when
available; do not infer a USB power fault solely from a lifetime timing maximum.
