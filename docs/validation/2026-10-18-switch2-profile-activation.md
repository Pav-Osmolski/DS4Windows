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

## Reporter follow-up

On 18 October 2026, [the reporter confirmed](https://github.com/Pav-Osmolski/DS4Windows/issues/48#issuecomment-6252913392)
that they had merged backups from a very old DS4Windows installation and saw
slot assignments getting mixed up when another physical controller was present.
A clean install allowed the Joy-Cons to connect after their DualSense Edge was
recognized. They could no longer reproduce the old configuration, so its exact
conflicting setting was not established. The issue is closed.

This confirms resolution for their clean installation; it does not establish
that PR #49 repaired the old slot configuration. The independently reproduced
profile-loading cases and regression coverage remain valid reasons to retain
[PR #49](https://github.com/Pav-Osmolski/DS4Windows/pull/49) as activation hardening.
See [migration guidance](../getting-started.md#moving-from-another-fork) for
backing up settings, testing a fresh configuration and restoring profiles
gradually.

Local full suites passed: 7,468 application tests with 12 existing opt-in skips,
and all 75 standalone tests. PR CI also passed packaging, installer lifecycle
and upgrade/profile-preservation checks before merging.
