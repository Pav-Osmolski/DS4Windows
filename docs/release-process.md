# Release process

New releases in this fork use numeric tags, starting with **5.0.14**. They
are normal GitHub releases with **Latest** selected and **Set as a
pre-release** cleared. The former `VIIPERRC4.x.x` names are historical.

For each release, advance all of these together:

- Release tag, package and informational version: `5.0.15`.
- Assembly, file, MSI product and installer bundle version: `5.0.15.0`.
- Installer: `DS4Windows_5.0.15.0_Setup_x64.exe`.
- Portable package: `DS4Windows_5.0.15_x64.zip`.

Every subsequent release increments the numeric version. Keep change lists
and upgrade notes in [CHANGELOG.md](../CHANGELOG.md); the README stays concise.
Use explicit Markdown links for every PR and issue reference in new changelog
entries (`[#123](https://github.com/Pav-Osmolski/DS4Windows/pull/123)`
for PRs, or `/issues/123` for issues). Check that these links work in the
entire rendered changelog before each release, including older entries and
its tagged copy. Verify each reference targets the correct repository and
uses `/pull/` for a PR or `/issues/` for an issue.

Always read and write Markdown and release-note files with an explicit UTF-8
encoding; never rely on the Windows default text encoding. Preserve existing
BOMs when editing files that use them. Run
`python utils/test-text-encoding.py --self-test` before release preparation,
and run it with the release-note file path before uploading notes. CI and the
draft release build reject invalid UTF-8 and common garbled punctuation/accent
sequences. The check reports problems without rewriting historical text.

Builds use the SDK in `global.json` and committed NuGet lock files. The draft
workflow runs both .NET suites against the checked-out tag before packaging.
For dependency or SDK changes, follow [dependency maintenance](dependency-maintenance.md#sdk-and-dependency-locks).

1. Merge reviewed changes and wait for the `main` CI tests, package checks and
   installer lifecycle/upgrade checks to pass.
2. Create a draft targeting that exact `main` commit. Use a three-part numeric
   tag such as `5.0.15` and the release title `DS4Windows 5.0.15`. Clear the
   prerelease flag and link the release body to the tagged `CHANGELOG.md`.
   Link each related issue/PR in the release notes, including upstream reports
   in their original repository. Verify the reporter's GitHub handle and thank
   them beside the relevant change. Check the title, links and credits against
   the previous release and source issues before publication.
   Credit follow-up suggestions in each release that implements them, even when
   the same reporter was thanked in an earlier release. Before publication,
   explicitly check each issue-driven change against its requester/reporter and
   confirm the thank-you appears in both the release body and changelog.
   In the GitHub release body, write credits as plain `@username` mentions so
   GitHub can recognize the contributor. In `CHANGELOG.md`, use profile links
   such as `[@username](https://github.com/username)` instead.
3. Run **.NET Release** from that same commit/branch, entering the draft tag.
   The workflow creates a missing tag only when the draft target matches the
   selected commit. It builds the portable ZIP, installer, matching source
   archives, a release-information ZIP, checksums and `RELEASE-BUILD.json` into the draft.
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

The matching VIIPER corresponding-source ZIP is bundled offline and copied
byte-for-byte into release records. The workflow checks its pinned hash and
the public VIIPER tag commit; the backend draft can remain unpublished during
combined hardware testing. See the [USB/IP upgrade guide](usbip-upgrade.md).

## Download files

### Diagnostic test previews

When the maintainer explicitly requests a portable diagnostic pre-release,
use a versioned tag such as `5.0.44-issue66-trace-preview20261022` on the
tested development commit. Mark it **Pre-release**, keep **Latest** unchanged,
and describe it as diagnostic work rather than a confirmed fix. These requested
test builds are unsigned; say so in the notes. They do not use the normal
stable installer/publication workflow, whose signing policy is unchanged.

Before publication, run the application and standalone test suites, validate
the portable package and pinned backend, and include matching source, SHA-256
checksums and a build record naming the exact commit and test results. Never
replace an existing published asset. Give clear logging instructions and
credit the reporter in both the notes and changelog. Keep the issue open until
the actual failure is understood and a fix is confirmed.

### Standard release downloads

Future releases upload seven assets: the installer, portable ZIP, matching
DS4Windows and VIIPER source ZIPs, `DS4Windows-<version>-RELEASE-INFO.zip`,
`SHA256SUMS.txt` and `RELEASE-BUILD.json`. GitHub also supplies its own automatic
source downloads; these are separate from the seven uploaded assets.

The release-information ZIP contains the licence, VIIPER licences, notices,
build notes, provenance and `SOURCE-REVISIONS.txt` previously uploaded separately.
The portable and installed distributions retain their existing bundled notices
and VIIPER source. Both matching source archives remain individually downloadable.
The build receipt records the information ZIP and each file inside it, and
publication checks their hashes and exact membership. Its schema remains 1;
the updater continues to use the standalone receipt and package digest.
Existing published releases retain their original assets and verification path.

Use this wording in release notes, without duplicating the asset links:

- **Most users:** download the `Setup_x64.exe` installer.
- **Portable use:** download the `_x64.zip` package.
- The remaining files contain source code, licensing information and verification records.
