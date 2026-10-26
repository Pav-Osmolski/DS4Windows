# Feedback allocation measurement investigation — 25 October 2026

## Trigger and limits

Main CI run [40194548045, attempt 1](https://github.com/Pav-Osmolski/DS4Windows/actions/runs/40194548045/attempts/1)
failed `PublishReadAndSpanSerializationAllocateZeroAfterWarmup`: 7,928 bytes
were reported across 20,000 calls after a 1,000-call warmup. The identical
production/test source passed the preceding PR suite and an isolated local run.
The main retry also passed the test gate. This alone does not explain the event.
There is no object-allocation trace from the hosted failure, so its exact bytes
are **unattributed**, not proven to be GC noise, JIT transitions or telemetry.

## Investigation

- Read the full production path: frame construction, mailbox publication/read,
  claim/completion, span serialization and parsing. It has no new changes in
  this release and no allocation was reproduced in the normal probe.
- The test called `CreatePatternFrame`, which called `Assert.IsTrue` on every
  measured iteration. MSTest 4.5.1 records assertion telemetry using a mutable
  concurrent dictionary. Its `DrainAssertionCallCounts` swaps that dictionary;
  the next assertion can allocate framework bookkeeping on the measured thread.
  [Exact dependency source](https://github.com/microsoft/testfx/blob/v4.5.1/src/TestFramework/TestFramework/Internal/TelemetryCollector.cs).
- A separate console probe called the actual original test method 100 times
  per condition: baseline 0 failures, throttled forced Gen0 GC 0 failures (21
  forced collections), background allocation pressure 0 failures.
- Deliberately draining MSTest telemetry on another thread every millisecond
  reproduced allocation failures in **23/100** original-test calls (72 bytes
  in the first examples, with no GC in those failed calls). This proves that
  the measured window includes framework work. It does **not** establish that
  a telemetry reset occurred in the original hosted failure.
- JIT disassembly of the cold local measuring method reported Tier0-FullOpts,
  not a captured OSR transition. Do not infer the previously documented OSR
  cases are the cause of this event.
- An initial unthrottled forced-GC probe was stopped because continuous explicit
  collections prevented a useful bounded comparison; it supplies no result.

## Test-only correction

Warm and measure the same assertion-free feedback cycle. Validate all operation
results and sequence round-trips, throwing on failure. Put framework assertions
outside the counters. Use the existing bounded `StrictAllocationMeasurementScope`
so invalidated no-GC reservations fail rather than silently weakening the check.
Keep the original **1,000 warmup / 20,000 measured** calls and exact **zero-byte**
limit. Add an independent row that creates a real 128-byte array inside the same
measurement and requires its allocation to be detected.

No controller code, runtime settings, production GC policy or allocation limit
changes. No retries or ignored failures are added to the test.

Local results: 32 feedback/measurement-scope tests passed. Revised probe batches
of 100 each passed baseline, background allocation, synthetic telemetry resets,
real-allocation control and telemetry-reset real-allocation control (500 total).
The telemetry-reset comparisons are an adversarial test, not a claim about
normal MSTest adapter timing. Require fresh default-runtime full CI on the final
revision before publishing DS4Windows 5.0.49.
