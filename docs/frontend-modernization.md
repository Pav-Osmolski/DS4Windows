# Frontend modernization

The frontend keeps the DS4Windows runtime and WPF binding layer intact while adopting
clear navigation, consistent spacing, descriptive cards, and progressive disclosure.

## Why the frontend remains WPF

DS4Windows has hundreds of mature WPF bindings and event handlers connected
directly to controller, profile, output-slot, automation, and diagnostic services. Replacing
that layer with Electron would require a second public API for nearly the entire program and
would make regressions easy to miss.

`BridgeShellStyles.xaml` provides the shared shell and component geometry, while
the existing view models remain the source of truth. Theme-specific colors stay in the
existing light and dark theme dictionaries, so runtime theme switching continues to work.

## Current navigation and feature coverage

Nothing in the existing UI has been removed. The main shell exposes:

- **Controllers**: connection type, access status, battery, selected profile, per-device
  profile linking, profile editing/creation, temporary lightbar color, and wireless
  disconnect behavior.
- **Profiles**: create, edit, rename, duplicate, delete, import, and export.
- **Auto Profiles**: application-driven profile and controller-slot switching.
- **Output Slots**: virtual controller slot inspection and control.
- **Settings**: ordinary startup, notification, charging, appearance, and update preferences.
- **Advanced settings**: VIIPER setup, OSC input/output, UDP server and smoothing, language,
  Steam/custom executable compatibility, process priority, absolute-mouse monitor, device
  registration, driver/update utilities, and diagnostics.
- **Log**: live status messages, export, clear, and detailed-message inspection.

The profile editor keeps the interactive controller mapping canvas and makes the dense
settings rail explicit:

- **Button Mapping**: complete button, stick, trigger, touch, gyro, keyboard, mouse, macro, and
  unbound mapping support.
- **Special Actions**: create, edit, remove, enable, and export action definitions.
- **Controller Readings**: live input, dead-zone, and drift inspection.
- **Axis Config**: left/right stick radial and axial dead zones, anti-dead zones, max zones,
  output curves, rotations, outer bindings, delta acceleration, flick stick, L2/R2 tuning,
  and six-axis acceleration.
- **Lightbar**: normal color, battery color, flash behavior, empty color, and passthrough.
- **Touchpad**: mouse, controls, mouse joystick, absolute mouse, passthrough, tap/double-tap,
  scroll, trackball, smoothing, inversion, and click behavior.
- **Gyro**: controls, mouse, mouse joystick, directional swipe, passthrough, steering wheel,
  trigger conditions, toggles, smoothing, jitter compensation, and inversion.
- **Advanced**: virtual output type and disable switch, output hooks, debouncing, rumble and
  DualSense rumble translation, controller speaker and microphone passthrough, mute-button
  lighting, input readout, mouse acceleration, touchpad toggle, DS4 output data, Game Bar,
  launch-with-profile, idle disconnect, wireless polling, and absolute-mouse options.

## Rules for the next UI passes

1. Backend settings remain authoritative; views do not maintain shadow copies.
2. A setting may move under **Advanced**, but it is not removed or silently reset.
3. Common pages use one title, one short description, flat content, and bordered cards.
4. Visible helper text is preferred to unexplained acronyms or tooltip-only documentation.
5. Device-specific controls remain visible only when their existing availability binding
   says the device supports them.
6. New features are isolated behind profile-backed services instead of being coupled to
   visual controls.

## Implemented feature services

Audio Haptics and Trigger Lab now have profile editor pages and dedicated
service/control implementations. Controller artwork also has project-owned
device-specific implementations. The original proposals for these features
must not be treated as outstanding roadmap tasks.

- `DS4Windows/DS4Control/AudioHapticsService.cs`
- `DS4Windows/DS4Forms/AudioHapticsControl.xaml`
- `DS4Windows/DS4Forms/TriggerLabControl.xaml`
- `DS4Windows/DS4Forms/ControllerArtwork.cs`

See the [user guide](user-guide.md) for the current profile navigation and
[roadmap](roadmap.md) for maintenance priorities.
