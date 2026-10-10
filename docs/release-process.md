# Release process

New releases in this fork use numeric tags, starting with **5.0.14**. They
are normal GitHub releases with **Latest** selected and **Set as a
pre-release** cleared. The former `VIIPERRC4.x.x` names are historical.

For each release, advance all of these together:

- Release tag, package and informational version: `5.0.14`.
- Assembly, file, MSI product and installer bundle version: `5.0.14.0`.
- Installer: `DS4Windows_5.0.14.0_Setup_x64.exe`.
- Portable package: `DS4Windows_5.0.14_x64.zip`.

Every subsequent release increments the numeric version. Keep change lists
and upgrade notes in [CHANGELOG.md](../CHANGELOG.md); the README stays concise.

1. Merge reviewed changes and wait for the `main` CI tests, package checks and
   installer lifecycle/upgrade checks to pass.
2. Create a draft targeting that exact `main` commit. Use a three-part numeric
   tag such as `5.0.14`, clear the prerelease flag, and link the release body to
   the tagged `CHANGELOG.md`.
3. Run **.NET Release** from that same commit/branch, entering the draft tag.
   The workflow creates a missing tag only when the draft target matches the
   selected commit. It builds the portable ZIP, installer, matching source
   archives, notices, checksums and `RELEASE-BUILD.json` into the draft.
4. Test those downloads. Existing release assets are never overwritten.
5. Publish the tested draft with **Latest** selected. The publication workflow
   verifies the successful draft run, source identity and every recorded asset
   hash without rebuilding, then explicitly selects that release as Latest.

These numeric releases are currently **unsigned**. Latest describes the
GitHub release channel; it does not claim an Authenticode signature. The
maintainer has approved unsigned numeric releases for this fork while trusted
signing is investigated separately. Include the signing status in release
notes. Source, digest, dependency and installer validation remain mandatory.
Other release types retain the certificate, signer and timestamp checks.

The receipt remains schema 1 for compatibility with DS4Updater 2.0.9.
Historical named RC receipts remain verifiable. Numeric releases carry a
four-part Windows version and a three-part release marker, so existing fork
builds can move from the RC channel to a newer normal release.
