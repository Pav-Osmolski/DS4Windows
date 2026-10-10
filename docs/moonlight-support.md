# Moonlight / Sunshine virtual controller support

[Documentation index](README.md) · [Troubleshooting](troubleshooting.md)

On the Windows PC running Sunshine and DS4Windows, open **Settings > Device
Options** and enable **Accept Moonlight / Sunshine virtual controllers**.
Connect from Moonlight with controller input enabled, then select a DS4Windows
profile for the detected input.

This accepts supported virtual HID controllers while the local Sunshine host
is running. A virtual device still needs a supported device identity and report
format; the option does not make every virtual controller compatible.
DS4Windows always excludes its own VIIPER outputs to prevent a feedback loop.
Physical supported controllers do not depend on this setting.

## Troubleshooting

- Confirm Sunshine is running on this PC and Windows sees the streamed controller.
- Enable the option on the Sunshine host, not merely on the Moonlight client.
- Check overlapping Steam Input/remapping and HidHide visibility if detection
  or game input is incorrect.
- If virtual input is repeatedly added or input loops, export the DS4Windows log
  and report the controller identity, profile output and connection procedure.

The old separate Advanced Support mode and global five-second connection delay
are not part of the current admission policy. Do not use their old instructions.

## Implementation

See [MoonlightVirtualDevicePolicy.cs](../DS4Windows/DS4Library/MoonlightVirtualDevicePolicy.cs),
[DS4Devices.cs](../DS4Windows/DS4Library/DS4Devices.cs), and
[policy tests](../DS4WindowsTests/MoonlightVirtualDevicePolicyTests.cs).
The current automated tests verify admission rules; they do not establish
streaming compatibility for every controller or host configuration.
