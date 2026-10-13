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
  Packaging restores only the application with its declared runtime graph and
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

## Follow-up implementation

- Package once from the proposed merge when a feature branch has an open PR.
  Tests still run on both HEAD and merge revisions. Default-branch pushes,
  manual runs and branches without PRs retain packages. Lookup errors fall back
  to building, and ten policy cases cover closed/unrelated/fork PRs and failures.
  Download the PR run's package when the branch build is skipped.
- Extract localized-control dispatch into `HeadlessWpfDispatcher`. It keeps the
  same process-lifetime STA thread and 30-second assertion; nested fixture calls
  execute directly on that thread. Existing nonparallel guards remain.
- Select the same reviewed .NET SDK locally and on hosted runners. Commit all
  16 NuGet graphs and enforce locked restore in CI. Dependabot proposes SDK
  servicing updates. See [dependency maintenance](dependency-maintenance.md)
  for regeneration and review instructions.
- Add installer-stage timings for manifest generation, both helper publishes,
  MSI and bundle composition, final bundle integrity and policy/payload checks.
  Native error checks, the repository mutex, forced rebuilds and all validations
  remain intact. No installer is installed on the developer's machine.

The existing installer log already narrows the bottleneck: on run 38386621163,
MSI construction took about 76s and bundle construction 69s, while both helper
publishes together took under ten seconds. Parallel helper compilation would
save little. Skipping MSI consistency or payload validation is not justified.

A manual warm-cache repeat of the unchanged `8992d7a` source is recorded in
[run 38391237735](https://github.com/Pav-Osmolski/DS4Windows/actions/runs/38391237735).
Restore took 15s for tests and 10s for packaging, versus 18s and 15s in that
revision's initial branch run; cache extraction itself took 9s in each job.
The test job fell from 4m01s to 3m25s. Hosted compilation/test variance contributes
to that difference, so it cannot all be attributed to caching.

## Scheduling-sensitive test correction

The first PR validation of this follow-up failed in
`XboxImpulseStopTraversesNinetyMillisecondBleReleaseEnvelope`; the branch run
passed on the same source. Its wait required at least four packets. A first
presentation callback after the 90ms fade legitimately emits the final neutral
as packet three, so the test timed out even with that valid sequence.

The fade test now drives the unchanged production presentation body at
controlled times under the original session gate. It checks decreasing physical
amplitudes through 80ms, exact neutral at 90ms, and late first callbacks at
90/160ms without replaying stale intermediate frames. A separate real-timer
integration test retains the two-second neutral deadline and the guard against
an immediate stop. The callback still obtains QPC time under the same lock;
only its existing presentation body was extracted into a private helper.
No tests are skipped or automatically retried to conceal this failure.

## Remaining recommendations

1. Collect more warm samples as ordinary changes run, rather than launching
   numerous redundant builds solely to improve benchmark confidence.
2. Use the new stage timings to investigate MSI/bundle compression and disk I/O
   before changing WiX behavior. Keep final payload hashes and lifecycle/upgrade
   execution on disposable hosted machines.
3. Isolate shared globals and native-process fixtures before attempting selective
   test parallelization. Do not remove elapsed-time recovery coverage or
   automatically rerun failures to make checks appear stable.

Fade assertions now have controlled-time coverage alongside the real-timer
check; installer validation has not been disabled to shorten runs. No
release workflow was dispatched during this audit because it writes release
assets. Its syntax and identity policy are checked locally; CI exercises
application publishing and installer construction.
