# Windows playback protection

The enabled-by-default `PreserveWindowsAudioOutput` app preference is exposed
under Settings > Components. It is unrelated to profile audio forwarding,
audio-to-vibration capture, endpoint visibility or the naming worker.

Before VIIPER creates an output, a dedicated MTA worker captures active render
endpoint IDs and console/multimedia defaults. Already virtual USB/IP defaults
are deliberately excluded as restore targets. The connection thread waits at
most 500 ms for the initial snapshot; unavailable/slow Windows audio fails open.
No audio API runs on an input, feedback or UI timer.

The lease binds to the stream's verified USB/IP port and controller slot. Only
a previously inactive endpoint on that exact port can trigger restoration.
Arrival/default notifications must be within two seconds, the lease must be
within its 30-second startup window, and the previous output must remain active.
Each role is consumed once; unrelated choices, stale notifications, disconnect,
successor streams and disabled protection cannot initiate later restores.
Writes recheck the current default and lifecycle. A later manual selection is
not continuously overridden. Windows does not expose automatic versus manual
origin, so an immediate manual selection during enumeration can be restored
once too; users wanting that behaviour can disable protection.

`IPolicyConfig.SetDefaultEndpoint` is isolated alongside the existing endpoint
label interop. Only console/multimedia render endpoints are supplied. Capture,
communications, descriptors, microphone processing, audio/haptic streams,
physical device visibility and USB/IP/driver configuration are untouched.
Endpoint errors are verbose-only diagnostics and do not fail a controller.
This uses Windows' compatibility COM interface, not a new supported public
default-device API; errors must remain non-fatal on future Windows builds.

Overview consumes an immutable notification-updated output snapshot. Routing
advice is shown only for a live DS4Windows-owned port, with the owning slot's
forwarding preference; unrelated virtual devices are not claimed. The Sound
settings shortcut opens Windows settings without changing a device itself.

Automated coverage includes per-role restore targets, one-shot behaviour,
late/reordered events, disconnected previous devices, other controllers,
existing endpoint selection, changed defaults, old-session notifications,
legacy preference defaults and XML round trips. Theme rendering tests inject
synthetic endpoint snapshots and inspect actual Overview controls in light and
dark themes without changing the user's Windows audio defaults.

Hardware acceptance: start with normal speakers/headphones selected, connect
USB and Bluetooth Sony controllers, verify that native haptics still work,
then select an already created virtual output intentionally with forwarding
off/on and check Overview guidance. Test Stop/Start, reconnect, multiple
controllers, unavailable previous output and disabled preservation. Do not
claim physical game-haptics quality from the synthetic policy tests alone.
