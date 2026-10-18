# Switch 2 profile activation — 18 October 2026

The report in [issue #48](https://github.com/Pav-Osmolski/DS4Windows/issues/48)
by @doyoulikegirlz reaches Bluetooth startup and then fails at profile/output
validation. Its excerpt cannot identify which profile/backend condition failed.

Production-stage probes with a simulated standalone Joy-Con Left reproduced
its original diagnostic for blank, missing, invalid and locked profiles. A
valid no-virtual-output profile activated. These probes used temporary profile
files and no physical Bluetooth or VIIPER device.

The fix validates before applying/resetting mappings. Only an unassigned,
unlinked slot may use the existing Default profile. Missing, invalid or
unreadable explicit profiles remain selected and are rejected with a reason.
No profile files are created or overwritten by the fallback itself. Legacy
profile migration retains its existing persistence behaviour. Profile failures
and missing virtual outputs have separate diagnostics; exact lifetime cleanup
and association handling are preserved.

Regression coverage runs the real profile stage with left/right runtime
adapters, confirms default selection and reconnect, preserves mapping state on
missing/invalid/locked files, forbids fallback for a missing linked profile,
checks retry after repairing an explicit profile, and fills output slots with
fake devices to distinguish output exhaustion without hardware access.

Reporter validation remains required: update to 5.0.31, test each half alone,
connect left then right and right then left, and repeat sleep/wake reconnects.
If activation fails, provide the full startup-to-failure log and selected/linked
profile details. Do not close the issue solely on the basis of automated tests.

Local full suites passed: 7,468 application tests with 12 existing opt-in skips,
and all 75 standalone tests. PR CI also passed packaging, installer lifecycle
and upgrade/profile-preservation checks before merging.
