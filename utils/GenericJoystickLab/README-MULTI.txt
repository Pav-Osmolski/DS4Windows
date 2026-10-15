Two-controller synthetic identity and reconnect lab

Extract the whole ZIP to a fresh folder. Close DS4Windows and all VIIPER
instances, then run START.cmd and accept the administrator prompt.
USB/IP 0.9.8.1 must already be installed. No physical controller is needed.
The test runs automatically. Send results/*-report.zip when finished.

Creates two temporary joysticks; checks distinct inputs in 32/64-bit
DirectInput and WinMM; disconnects one producer; removes/recreates devices
and reverses creation order. DirectInput identities must remain stable.
WinMM numeric slots may change. Existing USB/IP imports are refused.
Only owned imports are removed. No drivers or HidHide settings are changed.
Reports exclude authentication keys and configuration files.
Physical reconnects, HidHide and saved game bindings need separate tests.
Production VID/PID approval remains pending; this uses preview IDs.
