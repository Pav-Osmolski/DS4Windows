# Normal packaging preparation for generic joystick output

## Current design

The current feature uses a fixed sixteen-button device with per-profile numbered
assignments. See [the current design and migration rules](directinput-development.md#fixed-sixteen-button-design-2026-10-26).
Earlier dated evidence retains its original scope; native/game acceptance of the
sixteen-button candidate is still required before release.

This work prepares and checks the standard package paths while keeping generic
joystick activation restricted to the matched preview. The generated normal
artifacts are **packaging proofs**, not a generic-joystick release candidate.
They retain the compiled released VIIPER 0.1.11 pin; they must not be offered as
a replacement for the feature backend used by the working r8 preview.

## Implemented checks

- `utils/normal_package.py` reads the backend name/hash from the application's
  compiled source pin, checks the actual MSI/publish payload and sidecar, and
  compares both backend copies in the portable ZIP against that same pin.
- Portable and MSI/publish release markers must agree. Portable backend aliases
  and the portable marker must remain in the updater ownership manifest, while
  MSI/publish inputs must not contain those portable-only root paths.
- Both composition paths reject preview launchers, synthetic lab executables and
  lab-data directories before generating/replacing their outputs. The installer
  runs normal-package preflight before signing or constructing setup helpers.
- CI runs synthetic package regressions and checks its actual portable ZIP and
  MSI input tree. Existing complete installer provenance, source/license,
  localization and Burn validation remains in place.
- A regression exercises the real normal-launch `Connect` path: preview output
  is refused, remains disconnected and preserves saved joystick choices. It
  does not launch a backend or create an output device.

## Automated evidence and its limits

The package tests cover a backend replacement with a self-consistent but wrong
sidecar, stale sidecars, mismatched portable/extras copies, differing release
markers, lost upgrade ownership, preview contamination, duplicate/escaping ZIP
paths and preservation of existing output after refused composition.

The focused application run covers generic joystick behavior, HidHide policy and
ownership, backend maintenance, payload verification, portable broker lifetime,
release signing policy and uninstall helper planning. Existing setup simulations
cover upgrades/reboots, foreign-import preservation, startup-task ownership and
recovery, and the hosted MSI expected-version contract. These tests do not
install a driver or exercise normal physical controller cloaking.

Normal HidHide continues through the existing `EnsureHidHideForVirtualOutput`
path because generic joystick is a VIIPER output. Shared ownership/lifecycle
regressions are relevant to it, but a real installed generic-joystick HidHide
acceptance run remains outstanding. The portable preview deliberately bypasses
that integration and cannot substitute for this acceptance test.

## Coordinated backend promotion

Before generic joystick can be enabled in normal packages:

1. Adopt the approved production USB identity and decide the multi-controller
   compatibility scope recorded in the identity audit.
2. Produce the matching immutable VIIPER release, corresponding source,
   provenance and licensing payloads. A feature executable plus a regenerated
   sidecar is insufficient: the compiled application/bootstrapper pins must
   refer to the same approved bytes.
3. Update the release tuple together across the application setup manager,
   bootstrapper infrastructure probe, setup actions, embedded installer script,
   project content/resources, packaging/validation scripts and release workflow.
   Preserve existing hash, ownership, signing and recovery requirements.
4. Build and validate both standard formats. Then validate clean installation,
   upgrade/repair, actual built-in HidHide, output switching and exit cleanup on
   the matched candidate before approving normal activation or publication.

The USB/IP serial-completion fix remains a separate controlled retest when a
packaged fixed driver is available. See the [identity audit](directinput-identity-audit.md)
and [release checklist](directinput-release-checklist.md). No normal activation,
backend promotion, driver installation, merge or release occurs as part of this
packaging preparation.

### Read-only promotion rehearsal

`utils/prepare_viiper_promotion.py` checks a selected release before any pins
are edited. Supply an extracted Windows release directory containing
`viiper.exe`, `BUILD-INFO.json` and the five licence/notice files, its separate
corresponding-source ZIP, the independently verified tag/commit, a Go tool,
and a new output filename:

```powershell
python utils/prepare_viiper_promotion.py `
  --release-directory C:/candidate/windows `
  --source-archive C:/candidate/source.zip `
  --expected-tag $verifiedTag --expected-commit $verifiedCommit `
  --go C:/tools/go/bin/go.exe --output C:/candidate/promotion-plan.json
```

It reads embedded Go metadata without executing VIIPER, checks receipt identity,
binary size/hash, release build settings, notices and the source ZIP's commit
comment, and records the ten coordinated pin files with their current hashes.
It refuses an existing output file. CI exercises rejection of tampered inputs,
wrong versions/commits, debug builds, duplicate metadata and changed notices.

This is a consistency check, not proof that the source archive was built into
the binary. Independently verify the GitHub tag/source and retain the existing
PE, signing, provenance and installer validation. The plan explicitly remains
review-only and not release-ready; it never edits pins, starts VIIPER, installs
drivers or authorizes normal activation.

The 2026-10-15 rehearsal successfully checked the existing `v0.1.11` release at
`ab35e7f0cefd4e7145ac6638ee44fb40483634f0`. It proposed the same current binary,
not a new generic-joystick release. Its local plan is in the enclosing workspace
at `outputs/viiper-promotion-rehearsal/promotion-plan.json`.

### Saved-profile compatibility

Upgrade regressions exercise the actual profile serializer and file-save path.
Profiles without a joystick section retain every existing output (with existing
Xbox/DS4 alias normalization). Valid unused joystick options survive saving an
Xbox profile. The existing save policy discards an invalid inactive joystick
section, restoring default options on reload without changing the Xbox output.
If joystick output is selected, invalid settings instead refuse saving and
preserve the original file; they must not silently alter trigger resting axes.

## Validation result (2026-10-15)

Source revision `4fd58e63861cc12340e1dee1d269c85f0b67496f` produced both a normal
portable ZIP and a standard installer labelled `5.0.26-directinput-preparation`.
The installer passed MSI/Burn composition, extracted-bundle integrity and the
existing complete policy/payload/provenance validators. The normal preflight
verified both portable backend copies and MSI/publish input against compiled
VIIPER hash `949022AC8E30CABD2B06EBF92D38E34A2400BE6C31B417A53745BEE697A9C7A7`.

- Full application suite: 7,393 passed, 12 existing opt-in skips, no failures.
- Focused application subset: 408 passed, no skips (included in full total).
- New normal-package regressions: 11 passed.
- Existing localization suite: 35 run, one existing skip, no failures.
- Real package resource loading: two launch-directory cases passed.
- Upgrade/reboot, foreign-import cleanup, startup-task registration/backups,
  installer state-machine and 17 hosted MSI version-contract simulations passed.

Artifacts, hashes and a scope-labelled receipt are in the enclosing workspace's
`outputs/DirectInput-Normal-Package-Preparation-20261015` directory. These builds
were composed and inspected, not installed. No physical HidHide test, system
driver installation, normal feature activation or public release was performed.
GitHub CI runs separately; these numbers describe completed local checks.

### Promotion and upgrade preparation follow-up (2026-10-15)

- Read-only promotion tests: nine passed, including preservation of an existing
  plan before any tool invocation. The real `v0.1.11` metadata rehearsal passed.
- Normal-package regression suite: eleven passed; workflow actionlint passed.
- Full application suite after the first three upgrade tests: 7,396 passed,
  twelve existing opt-in skips, no failures. This includes existing HidHide
  ownership/cleanup coverage, not a physical cloaking test.
- Final focused upgrade suite: all four passed after adding the inactive invalid
  section case. Three overlap the full-suite result; do not add the totals.

Reports are in `outputs/directinput-promotion-tests` in the enclosing workspace.
No production pin, driver, activation gate or installed backend was changed.
