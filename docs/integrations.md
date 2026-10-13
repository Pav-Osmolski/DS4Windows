# Game Bar, motion servers and integrations

[Documentation index](README.md) · [Profile Editor](profile-editor.md#advanced) · [Troubleshooting](troubleshooting.md)

## Xbox Game Bar

In **Profile Editor → Advanced → Output**, enable **Game Bar controller
compatibility** and save. Xbox Game Bar must be installed; run DS4Windows
elevated as required by this integration.

While Game Bar is open, mapped input can use an Xbox-compatible companion.
Your selected profile and native output stay configured. Audio, haptics,
lighting and microphone routing remain on their existing paths. You do not need
to replace a native DualSense profile with an Xbox profile just to navigate Game Bar.

Test ordinary game input first, open Game Bar to check navigation, then close it
and check the game again. Avoid another mapper also supplying the same input.

## UDP motion output

For a client supporting the DS4Windows/Cemuhook motion protocol:

1. Open **Settings → Integrations → UDP server** and enable the server.
2. For a client on the same PC, use loopback address `127.0.0.1` and the configured
   port (normally `26760`). Enter the same address/port in the client.
3. Connect the controller and choose its slot in the client. Test motion before
   enabling optional smoothing.

The client must support this protocol; UDP motion output is not DSX mod input
or audio streaming. If using another PC, configure the appropriate host address
and network access deliberately. A loopback binding is reachable only locally.

## OSC control and monitoring

Open **Settings → Integrations → OSC control and monitoring**. The control server
receives supported OSC commands on its configured port. The monitoring service
sends to the configured destination address and port; it is a separate direction.
**Interpret Monitoring Messages** is an additional receiving option, not a
substitute for enabling the control server.

For a same-PC setup, use loopback addresses, configure matching ports in your
OSC application, and enable only the directions you need. Two receiving services
cannot share an already occupied endpoint. Check Log if startup fails.
Use the implemented message paths in the
[control service](../DS4Windows/DS4Control/ControlService.cs) and
[monitoring worker](../DS4Windows/DS4Control/OscMonitoringWorker.cs) when developing a client;
these are distinct from arbitrary UDP or DSX packets.

## DSX game mods

**Settings → Integrations → Game mod support (DSX)** accepts supported local mod
commands for physical DualSense/Edge triggers and lights. It does not receive
audio/PCM haptics. Follow the [DSX setup and priority rules](DSX-game-mod-support.md),
including resetting temporary overrides when the mod exits.

## Moonlight and Sunshine

On the Sunshine host, enable supported virtual-controller admission under
**Settings → Controllers → Device Options**. Follow the
[Moonlight/Sunshine guide](moonlight-support.md). This admission option does not
enable every virtual device, and DS4Windows excludes its own outputs to prevent
input loops.
