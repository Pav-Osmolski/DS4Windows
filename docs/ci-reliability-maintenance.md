# CI timing and warning maintenance — 2026-10-16

## Timing-sensitive Bluetooth software test

Run `38821892437` delivered all 250 distinct commands, preserved ordering and
media, and returned all native credits, but its real-thread queue reached 19/32
against the throughput assertion `< 16`. Maximum source admission lateness was
76.730 ms. The same commit's PR run passed. Scheduling delay is consistent with
these observations; the logs do not identify the specific host scheduling cause.

The default real-thread tests still require every command's exact bytes/order,
the final explicit stop, every finite media/haptics payload and complete credit
return. Existing bounded progress watchdogs remain. They record occupancy and
timing measurements without treating a shared runner as a controlled benchmark.

Three additional virtual-time cases drive the actual helper's wake selector,
FIFO claim/retirement, acknowledgements and production media clock. Sufficient
250/500 Hz and 500/1000 Hz offered/service budgets must remain bounded; the
500/200 Hz bottleneck control must fill capacity and delay the final stop.
No replacement rate-policy model or wall-clock sleeps are used in those cases.
The existing threaded writer/media tests continue to cover actual composition
and byte-level delivery, which the virtual-time scheduling test does not replace.

For a controlled-machine performance run, retain the original wall-clock
occupancy, effect-age, final-stop and media-gap thresholds explicitly:

```powershell
$env:DS4WINDOWS_STRICT_REALTIME_TESTS = '1'
try {
    dotnet test DS4WindowsTests/DS4WindowsTests.csproj -c Release -p:Platform=x64 `
        --filter FullyQualifiedName~DualSenseBluetoothNativeRateBudgetTests
} finally {
    Remove-Item Env:DS4WINDOWS_STRICT_REALTIME_TESTS
}
```

This is synthetic native I/O, not a Bluetooth latency or hardware throughput
measurement. No production scheduler rate, capacity or device behavior changed.

## Warnings

- Removed three genuinely unused private fields and an unused catch variable.
- Wired the existing input-slot-number notification into the same view-model
  refresh that already updates its other displayed slot properties.
- Removed the later duplicate Russian Program/RunAs/Task entries, preserving the
  first values the resource compiler already used.
- Corrected expected/actual assertion order and a misleading parameter name;
  modernized DynamicData discovery and the remaining obsolete DataTestMethod.
- Preserved literal protocol/default/enum guards. MSTest reports some of these
  as constant assertions (and some enum casts as always failing even though
  runtime comparison passes). Documented SuppressMessage exceptions are scoped
  to the individual contract tests; analyser diagnostics remain enabled for the
  rest of the project. No global NoWarn or broad severity reduction was added.
- Strengthened cached-serializer coverage to verify identity after repeated
  profile preparation, alongside the existing assembly-count check.

Updated CI and release workflow references to the supported Node 24 actions:
[checkout v7](https://github.com/actions/checkout),
[setup-dotnet v6](https://github.com/actions/setup-dotnet), and
[upload-artifact v7](https://github.com/actions/upload-artifact).
The small Linux package-planning and issue-label jobs are pinned to Ubuntu
24.04 rather than following the announced ubuntu-latest image migration.
Permissions, event triggers, package selection, signing requirements and payload
validation are retained. Workflow syntax is checked with actionlint; hosted CI
must verify the actual action execution.

## Local validation

- Full application suite: 7,377 passed, twelve existing opt-in skips, no failures.
- Standalone verification suite: 75 passed, no skips or failures.
- Focused timing class: seven passed, including all three virtual-time cases.
- Localization suite: 34 run, one existing skip, no failures.
- Actionlint passed for both modified workflows.
- Updated application/test compilation emitted no compiler or analyser warnings.

Results are retained in the enclosing workspace's `outputs/main-ci-cleanup`
directory. No physical controller, driver or installed backend was used for
these checks. Strict wall-clock benchmarking was not run as controlled hardware
performance acceptance.
