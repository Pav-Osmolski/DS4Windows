# usbip-win2 0.9.8.1 upgrade

DS4Windows 5.0.15 pairs VIIPER 0.1.10 with the official Windows x64
usbip-win2 **0.9.8.1** package. Older driver versions are not compatible with
this pairing. Use the matching bundled components together.

## Upgrade and restart

Close DS4Windows before running setup. When upgrading from 0.9.7.7, setup
removes the old driver and requires a restart before installing the new
driver. Follow the restart prompt, then resume setup. Do not start another
copy of DS4Windows during the upgrade. If Windows requests another restart
after installing the new driver, complete it before testing controllers.

The portable package also needs the matching system driver. Its backend setup
performs the same version and identity checks; copying a new executable over
an old driver installation is insufficient.

DS4Windows and VIIPER are unsigned. The bundled upstream installer is signed
by Cloudyne Systems (Scheibling Consulting AB), and its drivers carry Microsoft
Windows Hardware Compatibility Publisher signatures. This package does not
require enabling Windows test-signing.

## Integration changes

- VIIPER uses the new native attach layout, including the location hash and
  receive-mode field. Attach requests are 1,124 bytes; exact-location retry
  cancellation requests are 1,108 bytes. These layouts are checked against
  the tagged upstream headers and regression tests.
- Controller imports select **low-latency** reception. The equivalent CLI
  attach command specifies `--receive-mode=low-latency`.
- DS4Windows verifies the matching executable, library and both driver
  hashes before accepting the backend. Setup verifies the installer hash.
- Cleanup understands the new `usbip port` serial/mode lines while preserving
  exact endpoint, export and activation ownership checks. A foreign serial
  remains a reason to refuse cleanup.

The upstream updates include receive-path, cancellation and filter-driver
memory-safety fixes. Their implementation comes from the official driver
package; this fork does not replace or rebuild those kernel drivers.

## Known upstream limitation

The 0.9.8.1 release documents a broken serial-override feature introduced in
0.9.8.0. The fix was committed after the release. VIIPER leaves the override
empty and does not use it as proof of ownership. A returned port number alone
also does not authorize detachment.

## Test the combined package

After completing setup and restarts, test controller creation, input and
feedback for the output types you use. Exercise Stop/Start, unplug/reconnect,
profile changes and application restart. For Xbox One outputs, also test
activation retries. Confirm that another application's USB/IP import remains
attached throughout. Automated tests cover layouts, parsing and lifecycle
contracts; physical controller and game testing remains necessary.

Sources: [upstream release](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1),
[release notes](https://github.com/vadimgrn/usbip-win2/wiki/v.0.9.8.1),
[API notes](https://github.com/vadimgrn/usbip-win2/wiki/libusbip-v.0.9.8.1),
[post-release serial fix](https://github.com/vadimgrn/usbip-win2/commit/a2a1159763088c85d940078cfa6491f38540953d).
