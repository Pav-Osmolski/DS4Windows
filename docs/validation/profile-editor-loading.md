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
