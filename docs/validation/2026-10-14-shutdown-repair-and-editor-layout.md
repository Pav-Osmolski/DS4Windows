# 5.0.22 investigation: shutdown, repair diagnostics and editor layout

Date: 2026-10-14. Baseline: `0dc3daa` on Pav-Osmolski/DS4Windows `main`.
These changes are proposed for 5.0.22. DirectInput development is separate.

## Issue #25: service shutdown and DS3 reconnect

[Issue #25](https://github.com/Pav-Osmolski/DS4Windows/issues/25) reports a
Bluetooth DualShock 3 (SXS) with virtual Xbox 360 output. The virtual output
successfully disconnects before service shutdown stops making progress.
[The follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/25#issuecomment-6164907043)
also reports failure to reconnect after physical disconnection, and failure
of Stop/Start with the physical controller still connected.

The shared input registration table contains both legacy HID and Switch 2
runtime connections. ControlService first closes table admission through
the legacy authority and passes the full close snapshot to the Switch 2
coordinator. The coordinator previously treated a retiring DS3/DS4 entry
without a Switch 2 participant as missing cleanup authority. It quarantined
that entry and cached a failed close. ControlService returned before its
legacy retirement loop could run; subsequent Start could not finish reopening
the service. The last normal log message could therefore be “Closing VIIPER
virtual-controller connections” without a socket or VIIPER worker deadlock.

Regression tests reproduced this failure for both real DS3Device and
DS4Device types with controlled workers. Both failed with
`QuarantineRequired` before the patch. The coordinator now leaves valid
LegacyHid entries to their exact owner. The normal ControlService stop loop
continues to publish terminal neutral, join workers and remove registrations.

Coverage includes Stop/Start, a prior worker-stop timeout during disconnect,
mixed Switch 2/legacy connections, bound and quarantined legacy entries,
and rejection of a Switch 2 entry whose participant really is missing.
Quarantine is preserved when the legacy cleanup owner reports uncertainty.

Incomplete legacy disconnect cleanup is now reported in the normal log,
including its failure code. Previously those markers required the full verbose
file log and were absent from the reporter's pasted log.

The code bug is established; the reporter's complete physical disconnect /
reconnect failure is not yet reproduced with hardware. Test the patched build
with the same DS3 and profile. If reconnect still fails, obtain the full file
from Settings > Diagnostics > Open log after enabling Verbose logging, rather
than only the GUI log rows. Check the exact legacy retirement failure marker.

## Issue #20: incomplete USB/IP removal

[Rudey's follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/20#issuecomment-6157749493)
confirms two present devices with problem code zero and both USB/IP drivers
running after reboot. This is not a confirmed non-present record and is not
resolved by the 5.0.21 phantom-device classification change.

The exact-version uninstaller is invoked, then a reboot boundary and live
driver / DriverStore checks prevent overlay. The upstream 0.9.7.7 Inno setup
uses child devnode and pnputil commands in UninstallRun. Its package exit alone
does not establish that device removal succeeded. Its child commands do not
use the newer logoutput flag, so an Inno log may still lack child stdout.

Setup now passes a unique /LOG path under ProgramData/DS4Windows/Installer,
retains that path in the pending replacement marker, logs it again on resume,
and records the uninstaller exit code. Earlier logs survive later attempts.
Post-reboot device diagnostics now include hardware IDs. Old pending markers
without the optional log-path field remain readable. Safety checks and the
reboot boundary remain intact.

This is a diagnostic improvement, not a demonstrated fix for the reported
driver removal. The earlier full infrastructure/setup logs and old package
version are still needed. Do not delete the marker or force a live overlay.

## Layout fixes

- Launch-program checkbox and Browse button use matching margins and vertical
  centering; controls can wrap in narrow layouts. The application icon is an
  image and collapses when absent, eliminating the empty button-like square.
- Output Slots constrains horizontal scrolling while auto-fit can fit the
  columns. This avoids a transient scrollbar while DataGrid recalculates its
  star column after shrinking. Narrow and manual layouts retain scrolling.
  Resize tests exercise 171 widths in each theme and count transient scrollbar
  visibility changes, then check narrow/manual overflow.
- [The #19 follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/19#issuecomment-6165014034)
  concerns Show controller readings beside Axis Config. A 330-DIP dock leaves
  less space than the 292-DIP cards plus margins, scrollbar and theme gutter.
  Rendered output reproduced the clipped right borders. A 360-DIP dock fits
  them; stacked views, settings access and the same readings instance remain.
- Lightbar geometry is unchanged, as requested by the maintainer.

## Validation

The final application suite passed 7,326 tests with 12 existing hardware
opt-in skips. USB/IP reboot simulations passed all 15 post-reboot state cases
and replacement/ownership checks; infrastructure failure-summary checks passed
22 cases; startup task schema/ownership/rollback simulations also passed.
Local render review covers the launch row and the actual
docked Axis Config layout in light/dark themes and narrow/wide sizes.
No installed driver, Task Scheduler entry or controller was changed by these
tests. Physical controller and installer-repair confirmation remain pending.
