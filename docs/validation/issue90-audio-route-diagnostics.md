# Issue 90 audio-route diagnostic preview

Testing-only instrumentation based on 5.0.57 for [issue 90](https://github.com/Pav-Osmolski/DS4Windows/issues/90), reported by [1hezio](https://github.com/1hezio). This is not an echo fix. The maintainer has not reproduced it with DualSense; reporter uses physical DS4 v2 Bluetooth plus a headset in the controller.

## Consent and operation

Run `Start-Issue90-Diagnostic.cmd` from a fresh extracted portable folder. Fully exit the ordinary app first. The launcher explains recording/privacy, requires `I AGREE`, launches only this preview with a process-local capture flag and unique request nonce, then requires `RECORD` once the app, profile, game and NVIDIA recording are ready. Running DS4Windows.exe directly does not record audio. The request cannot be reused by a subsequent app process. No automatic upload, test-tone playback, driver changes or changes to audio settings/defaults are performed. The user briefly changes Headset/AUX volume manually as the timed instructions request, then restores it.

Capture is 30 seconds, Controller 1 only. Do not attach multiple controllers. Keep NVIDIA set to the named DS4Windows Controller 1 Microphone and separate tracks. First ten seconds: game playing, quiet. Next ten: only DS4Windows Headset/AUX volume zero, still quiet, leaving Windows/game/NVIDIA levels unchanged. Last ten: restore headphone volume and speak a short sentence. Transitions can restart audio routes; use the per-second settings, raw report/chunk timing and normal logs when analysing them.

## Captured stages

1. PCM arriving from VIIPER's virtual playback endpoint, before physical transport conversion.
2. Raw verified DS4 Bluetooth input reports carrying mic data, plus extracted incoming SBC frames with their sequences.
3. Decoded/resampled 48 kHz mono mic PCM before gain/noise processing.
4. The same PCM after processing/mute.
5. Final PCM after final mute and successful VIIPER microphone transport writes (DS4 16 kHz mono; DualSense 48 kHz stereo).
6. Windows' named virtual microphone via an independent WASAPI reader. No physical/default microphone fallback is used if the unique named device is unavailable.

The diagnostic reader adds a recording consumer, and can therefore activate the mic route. Keep NVIDIA recording too. This is disclosed in the diagnostic report. It cannot read NVIDIA's private source configuration; keep/share a screenshot and the original NVIDIA MP4 for the final comparison.

Each stage has bounded preallocated storage and a callback timestamp/offset/length timeline. Audio callbacks do not write files, allocate buffers or wait for busy storage; overflow/contention/format-change drops are counted. Storage capacity is approximately 62 MiB plus bounded metadata, never an unbounded recording. Export runs off the audio paths. WAVs concatenate captured chunks; their relative file positions alone are not hardware timestamps.

The ZIP also contains before/during/after endpoint IDs, defaults by role, mute/volume/mix formats and audio-session process IDs/names, copied active profiles before/after, per-second live audio settings, and up to the final 8 MiB of each available app/VIIPER log. These can contain paths/device identifiers and executable names; review before sharing. No unrelated audio endpoint is recorded.

`analysis.json` compares 10 ms RMS envelopes over +/-2 seconds. Missing/silent/invalid/short recordings report unavailable, not success. Strong correlation is a clue; it does not by itself prove digital mixing or a hardware fault. Raw samples permit more precise spectral/waveform comparisons offline.

## Interpretation

- Game audio already in decoded incoming mic: inspect raw Bluetooth reports/SBC extraction first; then distinguish controller/headset cross-talk from parser/decoder issues. This alone does not prove faulty hardware.
- Absent before processing but present after: inspect DS4Windows processing/state ownership.
- Clean internal stages, contaminated Windows capture: inspect virtual-device/Windows routing.
- Clean Windows capture, contaminated NVIDIA mic track: inspect NVIDIA capture selection/mixing.
- Missing/truncated/dropped stages or mismatched timing: the capture is incomplete; do not draw a clean/fixed conclusion.

## Verification

Synthetic cases cover delayed/scaled correlated signals, independent signals, silent/short/nonfinite input, byte and timeline limits, copy ownership, WAV export, non-blocking contention and zero-allocation disabled hooks. Existing audio/mic/feedback suites remain required. Full package CI must pass before publication. Physical DS4 confirmation is pending; do not merge to main or close #90 on this evidence alone.
