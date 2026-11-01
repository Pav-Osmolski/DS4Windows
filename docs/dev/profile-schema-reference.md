# Profile schema history

[Documentation index](../README.md)

This reference preserves early profile-format changes; it is not the app release
changelog or an exhaustive description of the current profile fields. Inspect
`DS4Windows/DS4Control/DTOXml/ProfileDTO.cs` and round-trip tests for the current schema.
App/file versions and profile `config_version` are separate values.

The sections below distinguish additive application changes from the older
numbered profile schemas. Additive fields do not require a schema-version bump.
The **config_version** attribute in a profile will describe the last known schema
used to write out a profile. Some older profiles did not contain the
**config_version** attribute so tags used in the profile might have to be examined
to determine an approximate version number corresponding to the schema version.

## Enabled special-action names (5.0.58)

`ProfileActionNames` contains individual `Action` string elements. Its presence,
including an empty array, is authoritative. `ProfileActions` retains its legacy
slash-separated form for older readers. When only that legacy field exists,
loading uses known action names to recover a unique partition; ambiguous or
unknown lists retain their legacy interpretation. This avoids splitting a name
such as `Play/Pause` in profiles saved by the new app.

## Multi Action timing and triple tap (5.0.58)

Multi-action `Details` retains its first three comma-separated macro fields
(single, hold, double). Optional fields append the triple macro, hold milliseconds
and tap-window milliseconds. Missing or invalid timing values default to 500 ms
and 150 ms. The UI requires a tap interval no longer than the hold duration.
Older apps can still read the first three macro fields but do not implement the
new triple/timing fields. Editing and saving in older versions can drop newer
fields; keep backups when switching versions.

## Multi Action names and enable switches (5.0.59)

The additive Multi Action extension uses 15 comma-separated fields. The
first six retain the above playback/timing layout; disabled gestures have empty
legacy playback fields. Using zero-based field indices, field 6 is
`v2:<enabled-mask>` (bits single, hold, double,
triple). Fields 7–10 hold Base64 UTF-8 display names, and 11–14 retain the four
recorded macros, including disabled ones. New readers restore those recordings
only for a recognized, complete extension. Invalid names fall back to empty
labels. Legacy actions default all gesture switches to enabled. Older versions
can execute the enabled legacy fields but may lose the extension when editing.

## Additional settings introduced in 5.0.29

These optional fields do not require a profile schema-version bump:

- `GyroStickActivationMode`: Disabled / WhileMoving / WhileCentered; missing
  values default to Disabled. `GyroActivationStick`: Left / Right / Either.
  `GyroStickThreshold` defaults to 15 and is clamped to 1-95.
- `PlayerIndicators` and `MicrophoneIndicator`: default to DeviceSettings when
  absent. Their runtime interpretation also uses the existing `LightbarMode`:
  inherited indicators follow application requests in Passthru. Explicit choices
  override the native values. Both DTO and legacy XML paths preserve the fields.

## Historical numbered profile schemas

These entries describe earlier DS4Windows releases rather than planned work.

### 5 (DS4Windows 2.1.15)

Replaced **UseTPforControls** with **TouchpadOutputMode**. The current application
supports controller controls, relative mouse and absolute mouse modes; see the
[Touchpad guide](../profile-editor.md#touchpad).

### 4 (DS4Windows 2.1.6)

Replaced **GyroSmoothing** and **GyroSmoothingWeight** with the
**GyroMouseSmoothingSettings** group, including 1 Euro Filter options.

### 3 (DS4Windows 2.1.3)

Version 3 was the first profile schema to have a **config_version**
specified. It was used primarily as a placeholder; the inherited history does
not establish a distinct field change from version 2.

### 2

Changed default values for **LSDeadZone** and **RSDeadZone**.
Added anti-dead zone info

### 1 (DS4Windows 1.4.52)

Initial schema as used in the last Jays2Kings build (1.4.52)
