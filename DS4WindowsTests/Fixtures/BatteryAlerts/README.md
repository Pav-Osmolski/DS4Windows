# Battery alert decoder fixtures

These files contain a generated 440 Hz sine tone, 0.12 seconds long. They are
test data produced for this project, with no third-party recording or music.

- `tone.wav`: stereo 44.1 kHz, 16-bit little-endian PCM.
- `tone.mp3`: the same signal, encoded as 64 kbit/s MP3.
- `tone.aiff`: the same signal, encoded as 16-bit big-endian PCM.

Tests decode and import these files without playing them through an audio device.
