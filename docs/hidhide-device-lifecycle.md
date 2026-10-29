# HidHide device lifecycle

DS4Windows treats a HidHide rule as ownership of a specific PnP generation,
not ownership of a controller name or of an entire USB container.

## Identity

- The exact HID instance ID resolved while the controller interface is live is
  always included.
- Container expansion follows HidHide Configuration Client semantics. HID
  siblings in the same non-system Container ID may be included. The base node
  is included only when it is HID/XUSB class and every immediate child is a
  positively identified HID in that same container.
- A mixed USB controller/audio base is therefore never blacklisted. A missing,
  unreadable, foreign-container, or non-HID child also makes the base
  ineligible.
- VIIPER outputs use different containers and are explicitly removed from the
  persistent blacklist after creation.

## Ownership and reconnect

Released HidHide 1.5 builds do not provide the process-lifetime session IOCTL,
so DS4Windows may have to add a persistent fallback rule. Only instance IDs
that this process actually inserted are removable ownership. A matching rule
that was already present protects the controller but remains user-owned.

The live identity set is captured before PnP removal. With reconnect protection
enabled (the default), DS4Windows retains its persistent additions until Stop or
exit. With protection disabled, it removes its additions after the disconnected
controller's virtual output has retired. Session entries are process-scoped and
are cleared only for a full service stop, never for one controller. Overlapping old/new generations are
reference-counted; if a reconnect arrives during a persistent removal write,
the rule is reasserted before the driver mutation boundary is released.

Stop closes a service-lifecycle generation before controller teardown. The
process-owned snapshot, binding invalidation, and process-wide session clear
then run under the same driver-mutation boundary. A HotPlug operation that
entered before Stop cannot add a rule afterward, and a later Start opens a new
generation only after cleanup completes.

A wired controller whose instance ID changes from A to B is resolved and hidden
as a new generation; retention of A cannot cloak an unknown B before discovery.
User-created HidHide entries are never adopted and deleted by DS4Windows.

Application-list inverse mode, active state, and blacklist reads are treated as
fallible driver queries. DS4Windows does not mutate whitelist policy, cache a
restore baseline, or erase its last verified affected-device snapshot unless
those queries succeed.

## Configuration-client application safety

Windows app-execution aliases (for example a WindowsApps Python launcher) are
not ordinary executable files. HidHide 1.5.230's configuration client can throw
an uncaught filesystem exception while displaying one in its Applications tab.
DS4Windows therefore inspects the file's reparse tag without following it
before registering startup or auto-profile applications. Confirmed aliases,
directories and unreadable application paths are not newly registered. Ordinary
file symlinks and junction paths retain their existing support. A read failure
does not authorize deleting an existing application permission.

The HidHide configuration button checks the current application list before
launching the external client. Confirmed app aliases are shown by their exact
paths, with an opt-in repair prompt that defaults to No. Nothing is pruned
silently, and missing ordinary applications are left alone. Inverse-list mode
is never automatically repaired because removal would alter its deny policy.

After confirmation, repair acquires the existing driver-mutation boundary and
an exclusive HidHide control handle. It requires the original application list,
inverse flag, active flag and hidden-device list to be unchanged. A unique,
flushed JSON backup of all four fields is written to `Backups/HidHide` in the
settings folder before any mutation. Alias metadata is checked again after
backup; only those exact entries are removed, then the full policy is read back
and compared. The control handle is closed before the GUI opens.

Unreadable state, changed state or failed backup prevents mutation. Failed
writes/readback are reported as uncertain rather than treated as success or
blindly rolled back; the backup path remains available. This configuration-only
reader validates bounded MULTI_SZ replies while preserving exact paths and the
released driver's empty-list/padding conventions. Entries containing unpaired
UTF-16 surrogates or other non-round-trippable policy text block repair before
backup or mutation, because the existing writer and JSON serializer would
otherwise change those code units. Valid surrogate pairs are preserved.
Runtime list readers and
input/haptics paths are unchanged. Filesystem revalidation is not a lock against
an external process deliberately replacing paths immediately afterward.

## Steam Input reclaim

Automatic Steam Input reclaim never invokes `pnputil /restart-device` for an
already active wired controller. Restarting the HID collection after the input
reader owns it can tear down the current generation, race the path/serial
registries, and return as a generic HID collection. A physical reconnect or
Steam restart is required when Steam held a wired controller before HidHide was
enabled. Bluetooth reclaim likewise avoids restarting an active device.

## Reconnect protection

**Settings > Controllers > Prevent double input > Keep controllers hidden between
reconnects** is enabled by default. It retains HidHide entries that DS4Windows
proved it added for a connected physical controller, even after that controller
disconnects. An already-known physical PnP identity can therefore reconnect with
its hiding rule still present, reducing the window in which Steam or a game can
open it before the virtual output is ready.

The policy is captured when the service starts. After changing the toggle, use
**Stop**, then **Start**. The setting itself performs no driver operations or
controller restarts from the UI thread. When disabled, the previous immediate
physical-disconnect release behavior remains available.

Normal **Stop** or application exit releases all retained DS4Windows-owned
entries, including controllers that are no longer connected. Preexisting
user-managed entries are never adopted as process-owned or removed. Cancelled
connections still roll back new additions; late release attempts revalidate
ownership and reassert a retained identity when necessary. An unsuccessful Stop
that resumes the service preserves the active retention policy.

This does not hide virtual controllers, change their identities, or alter input,
audio or haptics packets. Native controller use without DS4Windows requires Stop
or exit (or disabling the option and performing Stop/Start). It does not repair
input state already retained by a running game. It cannot guarantee hiding of a
previously unseen device identity before its first discovery, and it is not a
confirmed fix for GTA V Enhanced's duplicate-input behavior.
