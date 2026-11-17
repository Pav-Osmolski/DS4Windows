# Competing controller startups and bounded USB/IP repair

Setup reviews competing controller software before its MSI or shared USB/IP
repair changes anything. This covers other DS4Windows copies and VIIPER
backends, rather than assuming a particular fork caused the failure.

## Review before cleanup

The theme-aware installer page lists running DS4Windows/VIIPER processes,
enabled scheduled tasks, machine and original-user Run entries, and Startup
folder shortcuts. Executable paths and product metadata establish candidates;
paths, executable hashes, task definitions, registry value types, shortcut
bytes and process creation times form the exact review fingerprint.

The user can explicitly approve pausing verified entries and closing verified
processes owned by their account. Unknown paths, wrappers, inaccessible
processes, another user's tasks, password/service-account tasks and multi-action
tasks require manual review. The page offers Check again after the user resolves
them. It does not attribute a problem to hbashton or Nexus merely because one is
installed. It also does not inspect every other Windows user's private startup
folder or arbitrary scripts renamed to unrelated names.

Immediately before changes, elevated preflight repeats the inventory under the
shared setup mutex and compares it with the approved fingerprint. Differences
stop setup and require a fresh review. An unattended installation never grants
blanket approval to pause foreign software. Run values shared between registry
views are inspected once.

Before the first mutation, setup durably saves a protected receipt beneath
`%ProgramFiles%\DS4Windows.Setup\controller-conflicts-<id>`, a copy of the
self-contained recovery helper, and `RESTORE.txt`. The receipt preserves exact
Run values and kinds, task XML, and shortcut bytes. Startup entries are paused
before running processes are closed so a login startup cannot immediately
relaunch the old backend. Reserved root task names RunDS4Windows and RunVIIPER
are backed up and freed for the new installation; other tasks are disabled.
Applications and profiles are retained.

Recovery runs as administrator under the setup mutex. It restores missing
entries or re-enables unchanged tasks, refuses to overwrite newer entries, and
does not start applications. A newer managed task with the same reserved name
must be removed by uninstalling its owning installation before restoring the
previous one. Partial pause failures retain the receipt and its logged location.
The uninstaller only closes processes at its canonical managed paths, not other
copies of DS4Windows or VIIPER.

## USB/IP repair sequence

The specific diagnostic `Multiple instances of VHCI device interface found`
means the USB/IP client found multiple **present VHCI device interfaces**.
It does not by itself prove which app created them. Setup now distinguishes
this failure from a transient runtime/ABI restart request, including when the
installed package is already the supported version.

1. Close the reviewed competing processes and pause their approved startups.
2. Prefer the exact official usbip-win2 uninstaller. Preserve the existing
   uninstall log, reboot boundary and supported signed-package pins.
3. Restart Windows and verify removal before installing a replacement.
4. If verified leftovers remain, inspect their identities and complete device
   trees. Remove only exact USB/IP root instances and exact third-party
   `usbip2_ude.inf` / `usbip2_filter.inf` DriverStore packages whose provider is
   USBIP-WIN2. Use native Windows PnPUtil device/package removal without force.
5. Restart again after this fallback, verify removal, then admit the normal
   pinned-package installation and runtime check.

Imported or unclassified child devices, ambiguous package identities, failed
inspection, changed inventories or native removal failures stop automatic
cleanup. Empty USB root-hub infrastructure is allowed. An unverified or missing
USB/IP executable is never run to inspect imports; independent device-tree
inspection must establish that removal is safe. Verified clients retain the
existing ownership checks before detaching DS4Windows imports.

There is at most one residual-cleanup attempt per replacement transaction. The
attempt and boot identity are persisted before any removal, including failures
or partial cleanup. A further failed verification preserves diagnostics instead
of repeating cleanup indefinitely. Setup never force-deletes `.sys` files,
removes unrelated driver packages, changes signing policy or overlays a live
unverified driver ABI. Cases with only unidentified service/file remnants or
ambiguous uninstall registrations still require manual repair.

## Evidence and validation

MarioDLH's 5.0.75 log reported the supported USB/IP package passing verification
while `usbip.exe port` returned the duplicate-interface diagnostic. The older
[issue #20](https://github.com/Pav-Osmolski/DS4Windows/issues/20), reported by
[@rudey123](https://github.com/rudey123), recorded actual root devices remaining
after reboot. These are related repair scenarios, not proof of a shared cause.

The hbashton
[VIIPERRC4.6.7 setup source](https://github.com/hbashton/DS4Windows/blob/VIIPERRC4.6.7/extras/install-viiper-backend.ps1)
pins USB/IP 0.9.7.7 and VIIPER 0.1.9-rc4.6.6, so leaving that setup active can
reintroduce an incompatible shared component. Detection is based on observed
entries rather than hard-coded fork names or an assumption about Nexus's
current USB/IP version.

The removal strategy follows the official usbip-win2
[uninstallation guidance](https://github.com/vadimgrn/usbip-win2#uninstalling-usbip),
reviewed at tag `v.0.9.8.1`, commit
`55e1fa7f0c2157017b02dc1f3236e98169a535e4`. The exact hardware identity comes
from `drivers/ude/usbip2_ude.inf`; the duplicate-interface behavior comes from
`userspace/libusbip/src/vhci.cpp`. Native operations use documented
[PnPUtil commands](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax).

Automated coverage includes review fingerprint changes, process PID reuse,
backup-before-mutation, startups-before-processes, task principal/action/logon
restrictions, wrapper/UNC rejection, and XML external-entity refusal. PowerShell
simulations exercise exact device/package selection, multiple roots/packages,
empty hubs, foreign imports, inspection/stop/native failures, changed inventory,
bounded retry and both reboot boundaries. These tests mock all writes and native
removal calls; they do not modify the developer machine's drivers or startups.
