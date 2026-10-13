# Build and test audit — 2026-10-13

Scope: CI, release identity/publication, application publish, portable packaging,
WiX construction, translation probes, installer lifecycle/upgrade checks and
both .NET test suites. Baseline source: `3d3d180`; changes are in the 5.0.20 draft.

## Measured baseline

[Successful CI run 38383997108](https://github.com/Pav-Osmolski/DS4Windows/actions/runs/38383997108)
spent 4m24s in tests and 5m59s in packaging, approximately 10m25s including
the transition. Individual hosted step durations:

| Stage | Time | Observation |
| --- | ---: | --- |
| Test dependency restore | 43s | Whole solution, followed by implicit test restores |
| Main suite, including compilation | 3m06s | 7,301 passed; 12 hardware opt-in skips |
| Standalone verifier suite | 5s | 75 passed |
| Build dependency restore | 38s | Unnecessarily restores tests on the packaging runner |
| Application publish | 47s | Self-contained x64; repeats an implicit restore |
| Portable packaging | 15s | Manifests, translations, broker hashes and atomic ZIP |
| Translation probe | 6s | Packaged resources from different launch directories |
| Installer construction | 2m50s | Helper publishes, WiX builds and integrity validation |
| Offline layout | 3s | Bundle payload extraction |
| MSI lifecycle | 15s | Install, repair and uninstall |
| Previous-release upgrade | 33s | Profile preservation and version contract |

This is one hosted run, not an average or a promised speed improvement.
Push and PR runs validate different revisions: branch HEAD and the proposed
merge. Keeping both preserves branch artifacts and merge checks.

## Implemented improvements

- Cache only NuGet packages, keyed by project/SDK declarations and build inputs.
  Jobs still restore project assets. Binaries, test results, signing material
  and installer outputs are never reused from this cache.
- Explicitly restore each test graph, then run tests with `--no-restore`.
  Packaging restores only the application with matching x64 runtime and
  self-contained properties, then publishes with `--no-restore`.
- Cancel superseded CI runs within the same event and branch/PR. Separate
  event groups preserve branch and merge checks. Release runs are not
  automatically cancelled during asset publication.
- Bound CI jobs to 30 minutes and release jobs to 45 minutes. Both .NET suites
  have a five-minute inactivity hang watchdog. Preserve TRX reports and available
  hang diagnostics on failure, with 14-day retention.
- Run both .NET suites and localization-package tests on the checked-out release
  tag before composing release assets. Previously the release job did not run
  these suites despite its “build and test” comment. Native command failures
  exit immediately so a later success cannot hide them.
- Retain CI policy checks, package manifests, broker pinning, translation probes,
  WiX validation, lifecycle and previous-MSI upgrade checks. Signing, tag identity,
  asset receipts and exact-byte publication gates remain intact. Background CI
  installer processes start with hidden windows.

## Test reliability findings

Many tests share application globals, controller-service fixtures or native
processes. Broad parallel execution is unsafe until those fixtures are isolated.
Localized WPF controls also retain dispatcher ownership: using a temporary STA
thread can strand later localization work after that thread exits. Actual
localized controls use the existing persistent headless dispatcher. New Special
Actions tests also use it, checking long rows, activation binding and narrow
layouts in both themes.

Timing, teardown and broker tests contain deliberate delays and bounded waits.
Do not shorten them wholesale or automatically rerun failures: that can hide
lifetime/race regressions. Saved TRX durations allow targeted improvements.
Hardware integration remains opt-in; hosted checks cannot establish physical
device compatibility.

The local full run completed in 1m49s, excluding compilation: 7,305 passed and
12 skipped. Its slowest tests included ownership recovery after a five-second
gate timeout (5.11s), an absolute startup deadline under a dribbling consumer
(5.02s), and a real helper's paced native-source backlog check (3.37s). These
exercise elapsed-time and process behavior; replacing their waits with instant
mocks would remove coverage. This local timing is not comparable directly with
the hosted compile-plus-test step.

## Recommended next steps

1. Compare several warm-cache hosted runs with this baseline. Initial restores
   still download missing packages, and cache transfer has its own cost.
2. If runner usage becomes a concern, decide whether feature-branch artifacts
   are needed on every push with an open PR. Deduplication must still validate
   the proposed merge and preserve fork/Dependabot checks.
3. Profile installer construction before concurrent helper builds or incremental
   WiX reuse. The mutex, rebuilds, hashes and MSI validation protect against
   stale or mixed installers.
4. Extract the persistent UI dispatcher into a dedicated shared fixture as more
   localized-control tests are added. Separate deterministic tests from shared
   globals/native fixtures before considering selective parallelization.
5. Consider an SDK pin and NuGet lock files as a separate dependency-policy
   change. The floating .NET 8 servicing SDK receives fixes; a pin requires
   maintenance. Locking must cover installer SDKs, helpers and standalone tools.
6. Keep install/uninstall and upgrade execution on disposable hosted machines;
   local verification should not change the developer’s controller setup.

No assertions were relaxed or installer checks disabled to shorten runs. No
release workflow was dispatched during this audit because it writes release
assets. Its syntax and identity policy are checked locally; CI exercises
application publishing and installer construction.
