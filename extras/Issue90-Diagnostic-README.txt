ISSUE #90 - AUDIO ROUTING DIAGNOSTIC (TESTING ONLY)

1. Extract the complete ZIP into a NEW writable folder.
2. Fully exit your normal DS4Windows and other controller mappers.
3. Run Start-Issue90-Diagnostic.cmd. Read the recording notice and type I AGREE.
4. Import your affected Default.xml profile; connect ONLY the affected controller as Controller 1.
5. Use Xbox 360 emulation and the controller audio/microphone settings from your normal test.
6. In NVIDIA select DS4Windows Controller 1 Microphone and separate audio tracks. Start recording game audio.
7. Return to the launcher and type RECORD. It captures 30 seconds:
   0-10 seconds: game audio playing through headset; stay quiet.
   10-20 seconds: set ONLY DS4Windows Headset/AUX to 0%; stay quiet.
   20-30 seconds: restore Headset/AUX and speak a short sentence.
   Keep game audio playing while changing the volume. If the game pauses/mutes when you switch windows, tell us; do not interpret that as a successful isolation test.
8. Keep DS4Windows OPEN until the launcher says FINISHED. Explorer selects the diagnostic ZIP automatically.
9. Stop the NVIDIA recording. Review and share the diagnostic ZIP AND original NVIDIA MP4.

The ZIP is also under Diagnostics beside THIS preview's DS4Windows.exe.
It includes short speech/game recordings, raw controller reports, the active profile, audio device/session details, runtime settings and log tails.
Review before sharing. Nothing is uploaded automatically. Directly running DS4Windows.exe does NOT capture audio.
Only the separately named virtual microphone is recorded by the Windows reader; no physical/default input fallback is used.
The independent diagnostic mic reader adds a recording consumer and can activate the route. Keep NVIDIA recording too.
No audio defaults, saved settings, drivers or game files are changed by the recorder. Restore the Headset/AUX slider after the test.
If the capture fails, include the nonce-error.txt file from Diagnostics. Missing/empty stages are not proof the issue is fixed.
The automated correlation report is a clue only; the recordings need human analysis.
After testing, exit the preview and return to your normal installation. No installation or driver repair is required.
