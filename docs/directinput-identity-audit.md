# DirectInput joystick identity investigation

Date: 2026-10-15. Scope: the paired generic-joystick feature branches, USB/IP
0.9.8.1, and the owner's Windows build 26300. Status: input isolation validated;
DirectInput GUID persistence during output removal remains unresolved.

## Evidence

The r4 owner report `20261015-165810-0ed8a66d` contains 22 observations and 42
DirectInput device readings. All input patterns/counts/capabilities matched;
cleanup had no errors. Two observations failed GUID persistence, one per
architecture, when A was removed while B stayed attached.

Every opened-device query succeeded and agreed with enumeration. B kept its
USB serial and HID path, but its joystick ID changed from 1 to 0 and its GUID
became A's former GUID. Recreating A restored B's original slot and GUID.
Recreating both devices in reverse order also restored the original mapping.

A subsequent **read-only** inspection of the owner's current-user registry
found these stored values beneath
`System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput\VID_FFFF&PID_D10C\Calibration`:

| Calibration entry | Joystick Id | GUID key (raw bytes, same format as probe) |
| --- | --- | --- |
| 0 | 0 | `902A4ABF25C8F1118001444553540000` |
| 1 | 1 | `20F10323B6C8F1118001444553540000` |

All 42 native readings matched the stored GUID for their reported joystick ID.
The local evidence file is `outputs/directinput-slot-mapping-audit-20261015.json`
in the enclosing workspace, alongside a preserved copy of the owner report.
This registry inspection occurred **after** the run, not during hotplug. It
supports the slot/calibration association; it does not prove the internal
implementation of Microsoft's DirectInput or establish that USB/IP caused it.
No registry values, driver settings or device lifetimes were changed.

## Can the transport or descriptor pin the GUID?

VIIPER's native attach builder sends the export bus ID, localhost server address
and receive mode. Its serial override is intentionally empty for the pinned
0.9.8.1 release. The generic joystick's actual USB descriptor supplies the
per-controller serial, which Windows returned correctly throughout the report.

Reviewed code:

- [VIIPER attach builder](https://github.com/Pav-Osmolski/VIIPER/blob/15a8b4720964fd184ffa9e6474efe0f6584f0256/internal/server/api/autoattach_windows.go)
- [VIIPER generic descriptor](https://github.com/Pav-Osmolski/VIIPER/blob/15a8b4720964fd184ffa9e6474efe0f6584f0256/device/genericjoystick/descriptor.go)
- usbip-win2 `v.0.9.8.1`, commit
  `55e1fa7f0c2157017b02dc1f3236e98169a535e4`:
  [attach context](https://github.com/vadimgrn/usbip-win2/blob/55e1fa7f0c2157017b02dc1f3236e98169a535e4/drivers/ude/context.cpp),
  [descriptor forwarding](https://github.com/vadimgrn/usbip-win2/blob/55e1fa7f0c2157017b02dc1f3236e98169a535e4/drivers/ude/wsk_receive.cpp),
  [descriptor requests](https://github.com/vadimgrn/usbip-win2/blob/55e1fa7f0c2157017b02dc1f3236e98169a535e4/drivers/ude/device_ioctl.cpp).

No DirectInput GUID/joystick-slot control was found in these transport paths.
A serial override would target USB identity, not the independently observed
DirectInput slot reassignment. There is no evidence justifying a change to it.

## Documented Windows configuration interfaces

Microsoft documents GUID retrieval through
[GetConfig](https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nf-dinputd-idirectinputjoyconfig8-getconfig).
The documented flags for
[SetConfig](https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nf-dinputd-idirectinputjoyconfig8-setconfig)
cover hardware/type configuration, gain and callout; they do not include a
GUID-setting flag. Configuration changes require acquiring a global mode that
only one application can hold, as described in
[Acquire](https://learn.microsoft.com/en-us/windows/win32/api/dinputd/nf-dinputd-idirectinputjoyconfig8-acquire).
This research found no documented API that pins a caller-chosen per-controller
GUID through this removal scenario. That is a scoped finding, not proof that no
Windows solution exists.

## Options and recommendation

| Option | Assessment |
| --- | --- |
| Keep distinct serials and expose the limitation | Recommended for continued preview work. Preserves the validated isolation and accurately describes the outstanding risk. This does not waive production acceptance. |
| Keep a disconnected controller's virtual output present and neutral | Possible mitigation for physical disconnects, requiring explicit lifetime/ownership and cleanup work. Does not address deliberate output removal or app exit. Not implemented. |
| Rewrite calibration/GUID registry values on hotplug | Not recommended. Targets shared configuration and is not a demonstrated persistent fix. Not attempted. |
| Assign a different PID to every controller | Changes product identity/allocation policy; the current single-PID request does not cover it, and it has not been validated as a solution. Not implemented. |

Keep the GUID failure in the test result. Do not ask for another identical lab
run: it cannot answer a new question. An independent comparison with two other
identical-model joysticks, or an identified Windows/driver mapping change,
would be useful new evidence; neither has been performed. Actual saved-game
binding behavior remains unverified.

Continue release preparation with this limitation visible, the preview gate in
place, and the production multi-controller acceptance item open. Before normal
release, either validate a supported fix or explicitly decide the allowed
compatibility scope and document it. USB ID allocation, matched normal packaging
and built-in HidHide validation remain separate prerequisites.


## Pending USB/IP serial fix: controlled retest required

The published 0.9.8.1 [known issue](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1)
links [a2a1159](https://github.com/vadimgrn/usbip-win2/commit/a2a1159763088c85d940078cfa6491f38540953d).
The fix corrects serial-override descriptor completion/status and length handling,
and adds a fallback for successful requests left with pending USB status. On
2026-10-15 the latest published release remained 0.9.8.1; a release incorporating
the fix had not been published.

VIIPER currently leaves the USB/IP serial override empty and serves its own
USB serial descriptor. The successful serial queries in the reports do not prove
that Windows received or used the serial during initial PnP enumeration. Thus
the evidence makes the override bug a less direct explanation, but does not
exclude an effect from this commit, especially its shared completion change.
The earlier transport/API review is not a reason to close this retest item.

Retest when a suitable packaged driver containing the commit is available:

1. Verify the release's ancestry and record its version and artifact hashes.
2. Keep the same DS4Windows/VIIPER feature revisions and synthetic input patterns
   for the comparison; record any required transport compatibility changes.
3. Rerun the two-device lab, comparing GUID, opened-device metadata, slot, serial,
   path, input isolation and cleanup against r4 report `20261015-165810-0ed8a66d`.
4. Record the outcome even if only the known GUID failures remain. Do not change
   the GUID acceptance criterion to turn the comparison into a pass.

This is a release-preparation follow-up, not a claim that the fix will resolve
GUID reassignment. No driver was installed or replaced for this investigation.
