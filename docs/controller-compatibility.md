# Controllers, outputs and feature compatibility

[Documentation index](README.md) · [Getting started](getting-started.md) · [Troubleshooting](troubleshooting.md)

The **physical controller** provides your buttons, sensors and feedback hardware.
The **virtual output** is the controller DS4Windows presents to the game. Changing
output can improve game compatibility, but cannot add sensors, a speaker or
adaptive triggers to your physical controller.

## Supported input families

| Physical input | Connection and setup | Features to explore |
| --- | --- | --- |
| DualShock 3 | Requires compatible controller drivers; see Device Options | Button/stick mapping and rumble; do not assume DS4/DualSense feature parity |
| DualShock 4 | USB or Bluetooth | Touchpad, gyro, lightbar, rumble and supported controller-audio routes |
| DualSense / DualSense Edge | USB or Bluetooth | Touchpad, gyro, lighting, audio, advanced haptics and adaptive triggers |
| Switch Pro | USB or Bluetooth | Mapping, motion and Nintendo feedback/settings |
| Original Joy-Con | Bluetooth; standalone or a same-generation left/right pair | Holding styles, motion and Nintendo feedback; no Joy-Con 2 optical mouse sensor |
| Switch 2 Pro | USB or DS4Windows Bluetooth discovery/association | Nintendo motion/feedback; headset support over USB |
| Joy-Con 2 | DS4Windows Bluetooth discovery/association; standalone or a same-generation pair | Optical mouse, motion, dual Joy-Con aiming and Nintendo feedback |
| Supported Moonlight/Sunshine virtual input | Enable admission on the Sunshine host | Mapping of supported virtual HID identities; not arbitrary virtual devices |

These are supported families, not a guarantee for every third-party clone,
Bluetooth adapter or game. Device Options are under **Settings → Controllers**.
See [Nintendo setup](nintendo-options.md) and [Moonlight/Sunshine](moonlight-support.md)
for their distinct connection procedures.

## Controller polling

**Profile Editor → Advanced → Controller polling** shows whether DS4Windows can
request a different input reporting rate for the selected physical controller.
Currently this is implemented for the DualShock 4 protocol. Saving the profile
applies the selection on Bluetooth; it does not change USB polling. Offline
profiles can still configure and retain this value for a compatible controller.
**Automatic Bluetooth input rate** is enabled for new profiles and uses the
conservative DS4-compatible 250 Hz (4 ms) request. This is a default policy, not
a probe of the controller's supported rates. Turn Automatic off to request a
manual rate; switching it on retains the manual choice for later use. Existing
profiles with an explicit rate keep that manual selection. Changes take effect
when the profile is saved. This setting does not change VIIPER feedback pacing.

DualSense/Edge and Nintendo controllers use their backend's automatic reporting
behaviour; this setting does not add a configurable rate to those controllers.

Supported intervals cannot be reliably queried from every controller. Recognized
Sony DS4 IDs identify a known protocol, but copies can reuse those IDs. For an
unrecognized DS4-compatible identity the page marks the rates as unverified: the
controller may ignore a request. Even a recognized ID does not prove genuine
hardware or confirm that a requested rate was accepted. A measured arrival rate
can also vary with Bluetooth interference and host scheduling, so it is not a
reliable list of supported rates.

For a connected USB controller, the same section reads the physical device's
active configuration and negotiated speed through its parent USB hub. It shows
advertised HID interrupt input/output intervals separately, without guessing from
the controller name or applying an overclock. Composite devices are matched to
the HID interface rather than their audio endpoints. Ambiguous, inaccessible or
malformed descriptors display **unknown**. Reopening the editor after reconnecting
reads the current connection again. These are advertised intervals, not measured
arrival rates or proof that all alternative rates are supported. Driver filters
and host scheduling may change the rate actually observed.

Input reporting and feedback output have separate timing requirements. This
selection does not set VIIPER's virtual USB output interval or the Bluetooth
speaker/haptics packet cadence.

## Choose the controller the game should see

Open **Profile Editor → Advanced → Output** and choose **Emulated Controller**.

| Virtual output | When to choose it | Important limit |
| --- | --- | --- |
| Xbox 360 | A starting point for games expecting XInput | Does not expose PlayStation touchpad/audio interfaces to the game |
| Xbox One / Series | Games that support this output, including impulse-trigger feedback | Physical feedback still depends on the connected controller and translation settings |
| DualShock 4 | Games expecting a DS4, including compatible touchpad/lightbar/audio behaviour | Does not make a physical controller into a DualSense |
| DualSense / DualSense Edge | Games supporting PlayStation output and native DualSense features | Advanced haptics require a compatible audio path; adaptive resistance needs physical DualSense/Edge triggers |
| Switch 2 Pro | Software expecting the supported Nintendo output | Nintendo rumble translation does not reproduce adaptive-trigger resistance |
| Virtual controller disabled | Keyboard/mouse-only mappings or other deliberate configurations | Games receive no virtual gamepad from this profile |

Restart a game after changing output if it keeps using the old device. Configure
HidHide and overlapping Steam Input mappings so the game sees the intended pad.

## Hardware and connection limits

| Feature | Requirement or boundary |
| --- | --- |
| Native game haptics | A supporting game, compatible virtual output and physical feedback path; different from capturing game sound |
| Adaptive-trigger resistance | Physical DualSense/Edge; Trigger Lab can override the game's effects per trigger |
| PlayStation controller audio | USB uses Windows audio endpoints where required; Bluetooth uses the supported controller transport. Follow the model-specific controls in Advanced → Audio |
| Bluetooth microphone routing | Supported PlayStation microphone transport plus a compatible VIIPER virtual audio capture endpoint |
| Joy-Con optical mouse | Joy-Con 2 optical hardware; unavailable on original Joy-Cons |
| Dual Joy-Con aiming | A joined pair and separately configured motion output/activation |
| Switch 2 Pro headset/microphone | USB and a suitable headset; Bluetooth headset audio is unsupported |
| Game Bar compatibility | Xbox Game Bar installed; an optional mapped-input companion, not a change to the selected native output |

For detailed setup, use [Controller audio and feedback](controller-audio.md),
[Audio Haptics](user-guide.md#audio-haptics), [Trigger Lab](user-guide.md#trigger-lab)
and [Nintendo Options](nintendo-options.md). Technical transport contracts and
dated hardware investigations are linked from [Technical reference](reference.md);
their validation claims apply to the recorded hardware and build.

## Virtual controller battery reporting

The virtual DualShock 4, DualSense/Edge and Switch 2 Pro can receive the selected
physical controller's reliable battery and power state. Sony reports expose
approximately ten-percent bands; Nintendo reports use coarser bands. A source
without a reliable reading cannot provide a real percentage. Native DualSense
status passthrough remains authoritative, and physical Switch 2's existing
runtime-status route preserves its measured voltage.

Virtual Xbox 360 and Xbox One devices identify as wired USB controllers. Windows
and games can therefore show wired/no battery; this is different from reporting
the physical controller as fully charged. DirectInput and keyboard/mouse output
have no universal battery display. The DS4/DualSense browser tester does not
establish battery support for those other protocols.

A browser tester's **Factory info > Build Time** is the emulated controller's
firmware/factory date. It is independent of the DS4Windows application's build
or release date. The fixed, valid factory date is intentionally retained rather
than replaced with an invented physical manufacturing date.
