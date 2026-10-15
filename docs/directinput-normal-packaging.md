# Normal packaging preparation for generic joystick output

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
