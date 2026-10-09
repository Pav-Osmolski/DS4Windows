# Changelog

Release history for the Pav-Osmolski DS4Windows fork. Downloads, source
archives, checksums, and build records are available on [Releases](https://github.com/Pav-Osmolski/DS4Windows/releases).

## Unreleased — VIIPERRC4.6.8

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
is published. RC4.6.8 remains a draft for testing. The pinned VIIPER and USB/IP
packages are unchanged from RC4.6.7. Each new RC must increment the numeric
app and installer version.

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
