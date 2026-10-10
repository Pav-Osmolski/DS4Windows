# Changelog

Release history for the Pav-Osmolski DS4Windows fork. Downloads, source
archives, checksums, and build records are available on [Releases](https://github.com/Pav-Osmolski/DS4Windows/releases).

## Unreleased — 5.0.14

### Fixed

- Preserve the previous profile when a save is interrupted ([upstream #129](https://github.com/hbashton/DS4Windows/pull/129), meiameiameia).
- Keep virtual output slots registered when disconnect fails, allowing a retry without reusing the occupied slot ([upstream #130](https://github.com/hbashton/DS4Windows/pull/130), meiameiameia).
- Respect touchpad-click remapping and passthrough settings in emulated DS4, DualSense and DualSense Edge reports ([upstream #131](https://github.com/hbashton/DS4Windows/pull/131), meiameiameia).
- Remove the controller properly after a Bluetooth disconnect special action, allowing reconnection without Stop/Start ([upstream #132](https://github.com/hbashton/DS4Windows/pull/132), meiameiameia).
- Reuse the profile XML serializer to avoid repeatedly generating assemblies and reduce profile-switch overhead ([upstream #134](https://github.com/hbashton/DS4Windows/pull/134), petemess95).

### Changed

- Retired RC release names. New releases use numeric versions and are published as **Latest**, with the prerelease flag cleared.
- App and installer versions advance to **5.0.14.0**; release and package version **5.0.14**. Installer: `DS4Windows_5.0.14.0_Setup_x64.exe`; portable package: `DS4Windows_5.0.14_x64.zip`.
- Continue unsigned releases with exact-source build records and checksum verification. See the [release process](docs/release-process.md).

### Upgrade notes

DS4Updater 2.0.9 already supports numeric releases and the schema 1 build
receipt. Existing fork builds with fork update support can update to 5.0.14.
RC4.6.7 users still need one manual upgrade because that build checks upstream.
The pinned VIIPER and USB/IP packages are unchanged. Every new release must
increment the numeric app and installer version.

## VIIPERRC4.6.8 — tested build

### Changed

- App update checks and changelogs use `Pav-Osmolski/DS4Windows`.
- Updater discovery and downloads use `Pav-Osmolski/DS4Updater` and require DS4Updater 2.0.9 or newer for both portable and installed updates. Existing hash, size, release identity, and ownership checks are retained.
- App assembly/file and installer product/bundle versions advance to **5.0.13.0**. The setup filename is `DS4Windows_5.0.13.0_Setup_x64.exe`.
- Removed the Settings donation panel and its link to the upstream developer's PayPal account. Contributor credits remain.
- Shortened the README and moved release history into this changelog.

### Upgrade notes

RC4.6.7 users must install the first app build with fork update support
manually once. Replacing DS4Updater alone does not change RC4.6.7's upstream
update checks. Later updates from the new app use this fork.

DS4Updater [2.0.9](https://github.com/Pav-Osmolski/DS4Updater/releases/tag/v2.0.9)
is published. RC4.6.8 was tested successfully. Its changes are included in
5.0.14. The pinned VIIPER and USB/IP packages are unchanged from RC4.6.7.

## [VIIPERRC4.6.7](https://github.com/Pav-Osmolski/DS4Windows/releases/tag/VIIPERRC4.6.7) — 2026-10-09

### Fixed

- USB/IP driver verification timeouts at startup, reported in upstream issues [#118](https://github.com/hbashton/DS4Windows/issues/118) and [#126](https://github.com/hbashton/DS4Windows/issues/126).
- Windows version logging, including Windows 11 display versions and full build revisions.
- Missing profile editor navigation icons.
- Blurry system tray icons at small sizes and different display scaling levels.

### Changed

- Grouped translation folders under `Lang`.
- Updated fork download, setup, and project links and retained upstream contributor credits.

### Release notes

Unsigned Windows x64 release candidate with binary/installer version
**5.0.12.0**. Passed 7,155 automated tests (12 skipped, zero failures), plus
translation and installer lifecycle checks. The fixes were tested successfully
on the affected hardware.
