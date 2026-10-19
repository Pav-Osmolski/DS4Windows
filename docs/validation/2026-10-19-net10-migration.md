# .NET 10 migration validation - 19 October 2026

[Documentation index](../README.md) · [Development](../development.md)

This record covers the migration from main `4c9a85b` (.NET 8, DS4Windows
5.0.34) in [PR #56](https://github.com/Pav-Osmolski/DS4Windows/pull/56).
It does not qualify controller hardware or a later release.

## Build inputs and scope

- Pin SDK **10.0.402**, which publishes runtime **10.0.13**. Verify the SDK
  archive's SHA-512 against Microsoft's [release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
  before use. Keep exact SDK selection and locked dependency restore.
- Retarget all 14 C# projects, including installer hosts, tests and diagnostic
  tools. Preserve the application's `windows10.0.19041.0` API baseline.
- Keep x64 packages self-contained. Keep the existing workstation/concurrent GC
  settings, controller protocols, polling policy, VIIPER **0.1.12** and USB/IP
  **0.9.8.1**. No driver update is introduced by this migration.
- Retain direct application/library versions. Refresh all affected dependency
  locks for the new framework. .NET 10 prunes transitive framework packages;
  installer single-file tooling follows the SDK's ILLink **10.0.13**.
- Remove unused .NET 8 installation checks and obsolete download notices.
  Record the running runtime version in both saved and copyable startup logs.

## Compatibility review

- .NET 10 adds Windows Forms `ContextMenu` and `MenuItem` types. Profile Editor
  explicitly selects its existing WPF controls to avoid ambiguous names; menu
  behaviour is unchanged. See Microsoft's [breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10).
- Match `Marshal.QueryInterface` calls to the current `in Guid` signature;
  retain the existing native WinRT identity/lifetime test.
- WPF now rejects some [invalid dynamic-resource types](https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash)
  and [empty grid definitions](https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/empty-grid-definitions).
  Build and exercise the real light/dark resources and layout tests.
- The app remains a normal multi-file publish. The installer hosts remain
  single-file/self-extracting; review the changed [native-library search rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search)
  and exercise the built bundle's offline layout in addition to MSI lifecycle
  checks on an isolated CI machine.
- .NET 10 changes [XmlSerializer's handling of obsolete properties](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/xmlserializer-obsolete-properties).
  The profile DTOs have no such attributes; retain the full profile/settings
  round-trip suite as the gate.
- Both initial hosted CI runs exposed 24 bytes across 20,000 calls in the
  completed Switch 2 USB startup transaction allocation test, despite the first
  local full suite passing. A cold focused run reproduced it. A no-GC measurement
  scope alone still failed, so this was not resolved as GC accounting noise.
  JIT disassembly showed a Tier1-OSR transition inside the measured loop, with
  inlined Monitor paths. Separate diagnostic child processes disabling tiering,
  OSR or PGO each passed. Warm and measure the same optimized, non-inlined
  **test-driver** helper so that transition does not first occur in the measured
  loop. Retain the 1,000-call warmup, 20,000-call measurement and exact zero-byte
  assertion. Production methods, runtime/JIT/GC settings and controller behaviour
  are unchanged. Diagnostic runs are attribution evidence, not release gates;
  ordinary default-runtime focused and full-suite runs remain required.
- The subsequent full run also reproduced a 24-byte failure in the Bluetooth
  pacer saturated failure/reset storage test. A cold focused run reproduced it
  with a Tier1-OSR transition; separate no-OSR/no-PGO diagnostic processes passed.
  Optimizing only its cycle helper did not fix this case. Compile the saturated
  measuring test driver before its warmup instead, preventing OSR inside its
  counters. Keep the original warmup size, measured count, ownership checks and
  exact zero-byte limit. Its ordinary focused run then passed alongside the
  startup transaction and measurement-scope positive-control tests.
- A later full run exposed the same 24-byte cold-window observation in the
  pacer's completion-lease cycle gate. Its isolated default-runtime run passed,
  so the full-suite event has no captured object-allocation trace. Use the same
  pre-optimized measuring-driver policy for that warmed test too, retaining
  its zero-byte limit and all ownership assertions. The final full-suite and
  independent CI runs must pass with normal production runtime settings.
- The parallel branch run of `a509bf3` reported 98,352 counter bytes in the
  framed acknowledgement receiver's zero-allocation test, while the PR run and
  both fresh local diagnostic processes passed its zero/positive rows. No object
  trace was captured for that hosted event; it is not attributed as a new
  production allocation. Apply the same pre-optimized measuring-driver policy
  to that warmed gate, retaining its existing no-GC scope, exact zero check and
  real-allocation positive control. Require fresh full CI on the final revision.
- The next PR runner reported 3,840 counter bytes in the nominal speaker-frame
  gate while the parallel branch suite passed. That gate had only two warmup
  calls and omitted the bounded scope already used by other resampler gates.
  Pre-optimize its measuring driver and wrap only the measured counter pair in
  the existing strict scope. Add a non-inlined real heap-allocation positive
  control on the same workload. Keep the original 1,000 measured frames and
  zero-byte assertion; do not claim an object trace for the original CI event.

## Automated evidence

Local Windows 11 x64 checks:

| Check | Result |
| --- | --- |
| Application suite on .NET 10, including sidebar choices | 7,609 passed; 12 explicit opt-in tests skipped |
| Standalone hardware-verifier unit suite | 75 passed |
| Ten diagnostic-tool projects | Built successfully |
| Light/dark layout and related focused regressions | 170 passed; rendered theme output inspected |
| Sidebar icon persistence, defaults, picker and real shell layout | 15 passed; light/dark renders inspected at 100/150/200/300% |
| Locked restores | Application, tests, tools and installer projects passed |
| Localization/package unit suite | 33 passed; 1 environment-dependent test skipped |
| Text encoding self-tests and scan | Passed |
| Application NuGet audit, including transitive dependencies | No known vulnerabilities reported |
| Self-contained app, installer hosts, MSI and EXE bundle | Built; payload/policy validation passed |
| Built installer offline layout | Passed without installation |
| Packaged translation loading from package and outside directories | Both cases passed |

The existing CI workflow uses `global.json` and performs its own clean restore,
both complete test suites, portable packaging, translation probing, installer
layout, MSI install/repair/uninstall and upgrade/profile-preservation checks.
Consult the migration PR's checks for their current status. CI runs on Windows
Server 2022; passing it does not replace Windows 10/11 desktop testing.
The test-results artifact also includes the sidebar's light/dark scale renders.

## Desktop and hardware acceptance before release

These checks still require human testing of the .NET 10 package. Earlier testing
of 5.0.34's .NET 8 build does not qualify this migration.

1. Close the existing app, games and VIIPER. Use a separate extracted test
   package and a copy of your profiles. Confirm **Log > Copy Text** shows
   `.NET runtime: .NET 10...`.
2. On Windows 11 x64 and an explicitly recorded Windows 10 x64 edition/build,
   open Settings, Profile Editor, Log and Changelog in light and dark themes.
   Check resizing, links, numeric controls and theme switching.
   In **Settings > Appearance > Sidebar icon**, select each of the five icons,
   switch themes and move the window between differently scaled displays.
   Confirm the 60 × 60 layout stays sharp, then restart and verify the choice.
   Existing settings without a choice should start with **Blue Tile**, first
   in the list; the tray icon setting remains separate.
3. Load an existing profile, change a harmless setting, save and reopen it.
   Confirm mappings, gyro and polling selections remain correct.
4. With a DualSense over USB, emulate DualSense and use **Test Heavy** and
   **Test Light**. Check native vibration/triggers in DEATHLOOP and vibration
   in GTA V Enhanced. Repeat over Bluetooth, including disconnect/reconnect.
5. Check controller speaker output and Audio Haptics with a familiar sound;
   try both system and application capture, plus custom frequency filters.
   Confirm starting/stopping capture and exiting the game remain responsive.
6. On a disposable test machine/VM, install, upgrade, repair and uninstall the
   test installer. Confirm it needs no separate .NET runtime and preserves
   saved profiles. Avoid uninstall experiments on a working setup.

Record OS edition/build, transport, controller, test-build identity and the full
copied log with the results. Windows 10 support limits are described in
[Development](../development.md#net-10-compatibility).
