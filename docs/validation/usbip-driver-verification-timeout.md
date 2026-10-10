# USB/IP driver verification timeout: issues #118 and #126

> **Historical record.** This preserves the source, tests and limitations recorded at the time; it is not current setup guidance. See the [documentation index](../README.md).


## Finding

`EvaluateUsbipDriverIntegrity` previously found the two installed driver files
using `Win32_SystemDriver` queries. `CreateDependencyQueryOptions` sets a
two-second enumeration timeout. A WMI exception is caught and displayed as
`usbip-win2 driver integrity could not be verified: Timed out`, even before
the files can be hashed. This is a concrete failure path in DS4Windows, not
evidence that the driver files are corrupt.

Issue #118 reports intermittent success without changes to the installation.
Issue #126 reports that Settings Refresh succeeds after the startup failure.
Both observations are consistent with this query failure. They do not prove
why WMI timed out on those machines. The reported filter start-type change
and possible Sunshine conflict require separate runtime evidence; this patch
does not claim to fix or explain either.

## Change

Read `ImagePath` from the named driver's
`HKLM\SYSTEM\CurrentControlSet\Services` key instead of WMI. Microsoft documents
this value as the service binary path:
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/hklm-system-currentcontrolset-services-registry-tree

Resolve existing Windows driver path formats and hash the registered file.
Both hashes must still match the pinned USBip 0.9.7.7 values. Missing keys,
missing images, denied reads, and changed driver bytes cannot authorize the
backend. There is no fallback filename or hash cache. No service settings,
driver packages, or security settings are changed. The runtime USB/IP probe
and the Citrix safety check remain in place.

Only DS4Windows changes. No VIIPER or DS4Updater release is needed.

Source baseline: Pav-Osmolski/DS4Windows commit
`36502408cbba17fd20d84e6b258f2258714a39c6`.
Local branch: `fix/usbip-driver-verification-timeout`.

## Automated coverage

`ViiperSystemDriverIntegrityTests` covers actual file hashing, replacement on
the next inspection, missing service/image/file, denied configuration access,
and Windows driver path forms. Existing setup/readiness/probe tests verify
that startup still requires verified files and a working runtime.

Validation on 9 October 2026: the x64 Debug application and test project built
successfully using .NET SDK 9.0.318, targeting .NET 8. The focused run passed
all 95 tests (zero failures or skips) from `ViiperSystemDriverIntegrityTests`,
`ViiperSetupManagerTests`, `ViiperDependencyReadinessTests`,
`ViiperStartupReadinessTests`, and `ViiperUsbipProbeOutputTests`.
The build reported existing unused-member and duplicate translation warnings.

## Acceptance on an affected PC

1. Back up settings and profiles; close DS4Windows before replacing its files.
2. Test the candidate with the existing USBip and VIIPER installations.
3. Launch repeatedly, both normally and elevated. Confirm that the WMI driver
   integrity timeout no longer appears and virtual controller output works.
4. Reboot at least three times with the usual Run at Startup configuration.
   Confirm output works without Install / Repair or Settings Refresh.
5. If runtime readiness still fails, collect the DS4Windows log, `usbip.exe
   port` result, and the service configuration/state for `usbip2_ude` and
   `usbip2_filter`. A disabled or blocked driver can remain a separate problem.

Automated checks cannot establish successful reboot behavior on the reporter's
hardware. Do not treat this candidate as a confirmed field fix until that
acceptance test passes.
