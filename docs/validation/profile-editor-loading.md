# Profile Editor loading optimisation

## Scope and invariants

The initial Button Mapping workspace no longer constructs Axis Config,
Lightbar, Touchpad, Gyro, Audio Haptics, Trigger Lab, Nintendo Options or
Advanced. Each page has a compiled DataTemplate, creates its controls once on
first use and retains those controls until the editor closes. Every page's
ElementName references remain within its own namescope.

The loaded profile model remains authoritative for saving. This change does
not cache an entire editor across profiles, skip XML loading, move WPF controls
to worker threads, or change controller report/audio/haptic transport code.
Existing frozen controller-artwork caching remains in use.

Named controls are assigned before a page joins the visual tree. Startup value
notifications must not act as user edits or launch prerequisite setup. Preset
refresh rebinds existing axial/touchpad controls without duplicating handlers.
Closing retires deferred pages, stops the UI input timer and prevents late
device-discovery results from publishing into the old model. A failed editor
open also retires any constructed editor before removing it.

## Offline baseline

`utils/ProfileEditorLoadingProbe` runs against an explicit package directory.
It uses a plain WPF Application, an uninitialised offline service containing
empty device arrays, and a unique settings directory beside the report. It
never runs DS4Windows App.Startup or a ControlService constructor. It does not
start VIIPER, open controllers, install drivers or read the user's profiles.
The scratch directory is removed afterwards.

Build and run with the repository SDK:

```powershell
dotnet build utils/ProfileEditorLoadingProbe/ProfileEditorLoadingProbe.csproj -c Release
dotnet utils/ProfileEditorLoadingProbe/bin/Release/net10.0-windows10.0.19041.0/ProfileEditorLoadingProbe.dll <package-directory> <report.json>
```

Use `--visit-pages` to construct/render every settings page, revisit each page,
verify model ownership, detach/rebind for a preset refresh, save unchanged
settings, and edit a real RGB slider before navigating and saving again. XML
comparison includes the complete settings root, excluding only document-level
save-time comments. `--theme=Light` (or Dark, Graphite, Paper, Midnight,
HighContrast) selects the palette; `--profile-slot=0` exercises a physical slot
index with no physical device. This mode also writes page screenshots.

On the development machine, six isolated opening attempts against published
5.0.59 and the candidate produced:

| Measurement | Published 5.0.59 | Candidate |
| --- | ---: | ---: |
| Cold construction through offscreen render | 2,214 ms | 1,020 ms |
| Median of the next five opens | 851 ms | 116 ms |
| Median initial allocations on the UI thread | 274,812,144 bytes | 14,680,336 bytes |
| Settings pages constructed before navigation | 8 | 0 |

These are offscreen WPF layout/render measurements, not compositor presentation
latency or hardware acceptance. First visits to settings pages still incur
their construction/layout cost. Subsequent visits retain the same controls.
The probe does not exercise physical haptics, input polling or live endpoint
changes; those require the interactive checks below.

## Validation

- Application suite: 7,989 passed, 16 existing opt-in skips, zero failures.
- Focused final UI/profile/audio-haptics/trigger checks: 1,038 passed, three
  existing opt-in skips, zero failures.
- Deferred-page tests cover readiness, reentrant construction, retained edits,
  independent sessions, retired pages and binding initialisation.
- Offline page traversal, XML preservation, preset rebind and RGB editing
  checks run for the theme variants before delivery.

## Interactive acceptance

1. Enable Profile editor timing logs. Open/close the same profile several times;
   compare cold and repeated timings separately.
2. Visit every page. Switch away and back after changing a value or scroll
   position. Save, reopen and verify. Check Cancel and Apply separately.
3. Save a profile after visiting only Button Mapping; verify other settings
   remain unchanged. Repeat with a different profile and controller selection.
4. Open Axis Config and Touchpad, apply a preset, then verify both remain
   editable and live readings reflect the new values. Controller Readings must
   also work before Axis Config has ever been opened.
5. Check theme changes, keyboard navigation, tooltips, audio endpoint choices
   and USB endpoint details. Close the editor during background discovery and
   reopen a different profile; the previous session must not update it.
6. While playing over USB and Bluetooth, open/close the editor and verify input,
   audio and haptics continue normally. Do not treat an offline probe as proof
   of this hardware behaviour.

## Follow-up: real shell transitions (r2)

The user's r1 capture showed warm construction/attachment in 65–67 ms and
layout/Loaded in 93–105 ms, followed by 725–910 ms before the Background-priority
continuation. The isolated probe above does not reproduce that shell backlog;
its numbers are not end-to-end timings from the user's desktop.

The shell's selected-controller refresh also refreshed its hidden Audio Haptics
control. That synchronously enumerated Windows Core Audio endpoints, sessions
and process metadata. The control now defers hidden refreshes and runs session
discovery on a worker, publishing only plain snapshots on the dispatcher. A
pending scan is shared by overlapping refreshes. The result uses the current
profile selection and is not published into a hidden/unloaded page. This changes
the source picker, not capture, rendering, microphone forwarding or haptic
transport. No editable WPF control is constructed on a worker.

Opening now yields below both Loaded and Render priority before constructing the
editor, so the loading view can complete its layout first. Closing chooses the
return tab before restoring the profile browser, avoiding an intermediate
Profiles layout when returning to Controllers. Browser metadata/storage refresh
is skipped when Profiles is only being used as the editor workspace. Cancel
still clears previews and queues the regular-profile reload to discard edits.

Use `--check-audio-discovery` with the offline probe to inject a deliberately
blocked discovery operation. It checks that a hidden page does no scan, that a
visible scan leaves the dispatcher responsive, overlapping requests coalesce,
and late completion after hiding plus revisiting is safe. It opens only an
invisible offscreen test window and never reads real audio sessions in this check.

The timing option also records Cancel's preview cleanup, rollback queueing and
close handlers, then destination selection and return-page queue drain. Retest
repeated Controllers → Edit → Cancel with the actual controller and profile;
these measures can distinguish synchronous cleanup from destination layout if
any pause remains. User acceptance of r2 is still required.

## Follow-up: live lock contention (r3)

The r2 video/log still showed intermittent pauses: synchronous Cancel completed
in 11–19 ms on repeated opens, but the UI queue continuation could take around
900 ms. A local EventPipe sample trace of the running r2 app reproduced both
opening and Cancel delays. During the affected periods the UI thread waited in:

```
DispatcherTimer.FireTick
MainWindowsViewModel.RefreshRuntimeState
MainWindowsViewModel.CreateRuntimeSnapshot
ControlService.GetControllerRuntimeSignals
Monitor.Enter_Slowpath
```

The samples captured approximately 719 ms of this wait on opening and 890 ms
on return. Concurrent samples show the profile cold-options worker inside
DualSenseAudioPassthrough.Start / FindControllerEndpoint while its capture
callback also waits for the same runtime gate. Thus the delayed Background
continuation was evidence of UI blocking, not proof of a rendering bottleneck.
Hidden-picker discovery was unnecessary work, but did not explain this wait.

DualSense and DualShock 4 speaker status readers, plus the shared Audio Haptics
status reader, now take their runtime gate only if immediately available. When
busy they return the last complete status snapshot; a never-observed slot starts
as Starting. The next successful poll refreshes the snapshot, including failures
and stopped states. Invalid-slot behaviour is unchanged. This is display-only
observation, never authorization to use an audio runtime, and does not alter
capture, routing, teardown, packet cadence or haptic transport.

NonBlockingRuntimeStatusTests holds real service gates on another thread while
calling their actual status readers. Integration tests call the same
GetControllerRuntimeSignals path as the Overview timer for DualSense USB/BT and
DualShock 4 BT. Tests cover retained ready status during contention, cold starting
status, independent slots, exception-safe gate release, and fresh stopped/failed
observations after release. These tests use fake devices and no audio endpoints.
The initial contention/runtime-status selection passed 52 cases.

Retest the r3 local package with the same physical profile and audio settings.
Repeated Edit/Cancel timings should now remain close to layout/transition costs
rather than waiting for audio configuration. Retain the timing log if another
unrelated delay remains; do not present synthetic tests as hardware acceptance.

## Maintainer acceptance — 2026-11-01

The maintainer tested r3 with the same DualSense USB profile and accepted it
for release. The supplied log records first opening at 308 ms, repeated opens
at 167 and 137 ms, and return-page queue drain at 52, 69 and 58 ms. No previous
~900 ms stall appears in this capture. All eight settings pages were visited.
These are application timing phases from one setup, not compositor presentation
measurements or a claim of physical testing with every controller/transport.
The final local application suite passed 7,998 tests with 16 opt-in skips.
