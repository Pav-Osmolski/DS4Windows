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
