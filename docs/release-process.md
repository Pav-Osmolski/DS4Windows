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

Builds use the SDK in `global.json` and committed NuGet lock files. Main CI
runs both .NET suites and validates the final portable and installer packages.
The normal release workflow promotes those exact tested bytes without rebuilding.
For dependency or SDK changes, follow [dependency maintenance](dependency-maintenance.md#sdk-and-dependency-locks).

1. Commit the intended new numeric version before merging. Merge reviewed
   changes and wait for the `main` CI **checks** gate, package checks and
   installer lifecycle/upgrade checks to pass. Keep the CI run ID. Its
   **DS4Windows_Release_Candidate_...** artifact contains the final-version
   portable ZIP, installer and `CANDIDATE-BUILD.json`; retention is 30 days.
   Temporary artifact cleanup preserves this promotion window and the latest
   branch packages; see [Actions artifact retention](actions-artifact-retention.md).
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
3. Run **.NET Release** from **main**, entering the draft tag, **mode: promote**,
   and the successful main **candidate_run_id**. The selected workflow commit,
   tag, candidate source commit and embedded versions must agree exactly.
   The workflow creates a missing tag only when the draft target matches the
   selected commit. It verifies the successful same-repository main run, every
   required test/installer gate, the exact run attempt, artifact digest and
   package hashes. It uploads the original portable and installer bytes,
   matching source archives, release-information ZIP, checksums and
   `RELEASE-BUILD.json` into the draft. Branch and PR artifacts are never eligible.
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

### Explicit rebuild fallback

If the candidate is missing or expired, run **CI Build** from the intended main
commit with **package: installer** to produce and validate a fresh candidate,
then promote that explicit run ID. There is no automatic search for a newer
artifact, another branch or an older attempt. If a documentation-only commit
becomes the intended release source, this manual main build is also required.

Alternatively select **mode: rebuild** in .NET Release. This deliberately runs
the full test/build/package path again, including installer layout, install,
repair, uninstall and both upgrade baselines. It is an explicit recovery path,
not the default. Existing assets are never overwritten in either mode.

The schema-1 release receipt remains compatible with DS4Updater 2.0.9.
Promoted releases additionally record the candidate CI run, attempt and artifact
ID. The receipt's `runId` identifies the successful release-preparation run;
`candidateRunId` identifies the original binary-producing main CI run.

### Branches, previews and required checks

- Branches without an open PR run the application test suite. When a matching
  same-repository PR is open, its proposed-merge validation replaces duplicate
  push tests. An API lookup failure retains branch tests.
- Ordinary PRs run tests. Installer, dependency, workflow and infrastructure
  changes also build and validate both packages before merging. An unknown
  changed-file set takes the full pre-merge package path.
- To get a preview, run **CI Build** on the desired branch with **package:
  portable** (default) or **installer**. This runs the full test gate first.
  WIP branches remain independent; syncing main into one requires fresh tests.
- Known documentation-only changes run text/site validation. The always-present
  **checks** job fails if any selected CI gate fails or is cancelled, and can
  be required by main's branch protection without waiting for skipped jobs.
- Main application/infrastructure changes build once with final numeric
  identities. CI publishes one candidate artifact rather than duplicate copies
  of its packages. Already-compressed package artifacts use no outer compression.
- Cancel superseded development runs within their own branch/event or PR.
  Release preparation/publication is serialized per release tag and is not
  cancelled by WIP activity. Documentation publication remains separate.

Both the legacy **RC4.5.5** baseline and numeric **5.0.43** baseline exercise
application-only MSI upgrade, repair, uninstall and preservation of test
profiles on disposable hosted Windows runners. Their installers and MSI
payloads have immutable hash pins. Update the numeric baseline deliberately
as releases progress; do not replace it with a moving `latest` URL. These
fixtures never install drivers or run on a developer's computer.

Packaging uses Python 3.12, matching the documentation workflow and the hosted
runner cache. The full application suite remains the release gate; optimising
duplicate work does not mean reducing test coverage.

The receipt remains schema 1 for compatibility with DS4Updater 2.0.9.
Historical named RC receipts remain verifiable. Numeric releases carry a
four-part Windows version and a three-part release marker, so existing fork
builds can move from the RC channel to a newer normal release.

The matching VIIPER corresponding-source ZIP is bundled offline and copied
byte-for-byte into release records. The workflow checks its pinned hash and
the public VIIPER tag commit; the backend draft can remain unpublished during
combined hardware testing. See the [USB/IP upgrade guide](usbip-upgrade.md).

## Download files

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
