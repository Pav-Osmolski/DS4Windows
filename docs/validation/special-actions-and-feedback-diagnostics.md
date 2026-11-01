# Special actions and feedback diagnostics

This change addresses [#99](https://github.com/Pav-Osmolski/DS4Windows/issues/99)
and the requests in [#100](https://github.com/Pav-Osmolski/DS4Windows/issues/100).
It adds observations for [#98](https://github.com/Pav-Osmolski/DS4Windows/issues/98),
without claiming to fix that un-reproduced haptics report.

## Automated coverage

- Actual profile XML round trips preserve slashes, quotes, Unicode, multiple
  enabled names and explicit empty selections, including editor slot 8.
- Legacy profiles retain slash-separated support. Known names are recovered
  only when exactly one interpretation exists; unknown/ambiguous lists keep
  their legacy interpretation. Compatibility recovery has size/depth bounds.
- Monotonic gesture tests cover exclusive single/double/triple/hold actions,
  configurable thresholds, legacy defaults, controller isolation and retirement
  of pending gestures after profile changes. UI-side resets publish a generation;
  only the input owner mutates its gesture state.
- Import tests preserve existing actions, rename collisions, retain new gesture
  fields and create a backup on atomic replacement. Invalid XML, external
  entities, unknown action types/triggers and malformed key codes are rejected.
- The actual multi-action editor layout is rendered in light/dark themes at
  narrow/wide widths. Timing label/input pairs wrap together, and the editor can
  scroll when necessary. Shared themed button styles are used.
- Feedback observation tests distinguish status from control/media frames and
  non-zero motor/rear-channel requests from LED-only or silent packets. Disabled
  verbose logging does not observe payloads or schedule summaries. The app checks
  verbose before allocating the observer or reading a diagnostic clock, and
  queued publication checks verbose and stream identity again.

## Maintainer acceptance

1. Back up settings/profiles and extract the local preview into a separate
   folder. Fully exit the normal app first. A portable copy can still select
   existing AppData settings; work on a duplicate test profile.
2. Create/enable a `Play/Pause` action, save and reopen. Repeat with Apply,
   multiple actions, and clearing every enabled action.
3. Record simple A/B/C/D macros for single/hold/double/triple and test in Notepad.
   A comfortable initial setup is hold 0.75 seconds and tap interval 0.25 seconds.
   Confirm each gesture produces only its assigned macro. Remove the triple
   macro and confirm double tap no longer waits for a possible third tap.
4. Export actions, then import that file. Existing definitions must remain;
   duplicates get an imported suffix and newly imported actions stay disabled.
   Review imported macros/program actions before enabling them.
5. Use the editor's Settings shortcut and verify Save, Discard and Cancel.
6. With verbose off, confirm there are no `VIIPER feedback path` summaries.
   With verbose on, trigger game feedback and compare frame/request observations
   with existing physical feedback-health logs. A microphone-state frame alone
   is not haptics. A callback is not an actuator acknowledgement.
7. Retest ordinary feedback over USB and Bluetooth. Synthetic tests cannot
   replace hardware/game testing, or prove that #98's reported failure is fixed.

No VIIPER build, driver changes, audio recording, or #90 diagnostic hooks are
included in this change. Release and issue closure remain separate from local
preview qualification.
