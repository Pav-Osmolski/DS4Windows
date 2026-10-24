# Generic joystick pre-release review — 2026-10-15

Result: the matched preview has positive single-controller evidence. Normal
release is still gated; USB ID allocation alone does not complete acceptance.

## Revisions and CI reviewed

| Repository | Feature revision reviewed | Latest main contained |
|---|---|---|
| DS4Windows | `80da85c745d1bfaffc791afe37b010325ac16202` | `4b4b8625495dc9375a04fc6e396fb6184b716fa5` |
| VIIPER | `bbf35445183ad6bf7ffb348279ef8ed50a2d9e7f` | `ab35e7f0cefd4e7145ac6638ee44fb40483634f0` |

Both working trees were clean at the start; fetching main showed zero commits
missing from either feature branch. Both PRs were open, draft and mergeable.

- VIIPER: [PR checks](https://github.com/Pav-Osmolski/VIIPER/actions/runs/38775344789)
  and [client generation/builds](https://github.com/Pav-Osmolski/VIIPER/actions/runs/38775344728)
  passed at the reviewed revision, including Windows/Linux builds and generated
  TypeScript, C#, C++ and Rust clients.
- DS4Windows: the preceding code revision `f14913c` passed
  [branch CI](https://github.com/Pav-Osmolski/DS4Windows/actions/runs/38775330940)
  and [proposed-merge CI](https://github.com/Pav-Osmolski/DS4Windows/actions/runs/38775340218).
  Its subsequent documentation-only `80da85c` runs were still in progress when
  this review began; no pending run is counted as passed here. The packaging
  correction below requires CI at its own revision too.

## Source review and correction

Reviewed DS4Windows output selection, profile serialization/defaults, mapping,
immutable option/identity reuse, native readiness/contract checks, producer
recovery, normal-mode gate, shared HidHide classification and package verification.
Reviewed VIIPER descriptor/wire ranges, explicit experimental options, serial
identity, single-producer lease, neutral fences, import generation/readiness,
accepted USB configuration observation, and client generation changes.

One additional defect was reproduced in the new normal-package validator: it
verified the canonical backend entry but accepted extra ZIP entries with dot,
empty or Windows trailing-dot/space path components. Such names can alias a
verified file during extraction. The validator now rejects those ambiguous
components before hash checks. Regression cases failed before the correction
and pass afterward; explicit normal directory entries remain accepted.

Validation of the correction: thirteen normal-package tests and nine promotion
tests passed. The existing real normal portable ZIP and MSI publish input passed
the stricter validator with the unchanged compiled VIIPER 0.1.11 hash. No app,
backend or driver was launched, and the application mapping/descriptor did not
change. Existing application tests and hardware evidence are recorded in the
[normal packaging evidence](directinput-normal-packaging.md) and
[development log](directinput-development.md); they were not redundantly rerun
for this Python-only correction.

No further actionable defect was identified in the reviewed paths. This is a
source review plus existing test evidence, not a claim of exhaustive correctness
or physical validation of normal installation.

## Remaining release requirements

| Requirement | Current evidence / next action |
|---|---|
| Production USB identity | pid.codes PR #1317 remains open without reviews; proposed `1209:D10C` must not be used until assigned. Adopt only the approved twelve-button identity, then repeat enumeration/resting-axis/binding acceptance with it. |
| Matched normal backend | Normal packages intentionally retain released VIIPER 0.1.11 and reject joystick activation. Produce verified feature release assets, promote all coordinated pins and validate both normal formats before enabling output. |
| Installed integration | Clean install, upgrade/repair and actual built-in HidHide remain untested for generic output. Follow the installed acceptance checklist; portable preview cannot prove cloaking or hiding-policy restoration. |
| Multi-controller bindings | Input isolation passed, but removing A changed B's DirectInput GUID/slot. Resolve with a supported validated change or obtain explicit approval of a documented initial compatibility scope. Do not claim stable multi-controller saved bindings. |
| USB/IP retest | Latest published release checked remains v.0.9.8.1. When a packaged driver containing the upstream serial-completion fix exists, verify inclusion and repeat the controlled two-device comparison. A future fix is not assumed to resolve the GUID issue. |
| Candidate approval | Repeat required CI and owner acceptance on the actual matched production-identity candidate before merge/release. Sixteen buttons remain lab-only. |

The owner-reported r8 BeamNG/FlatOut 2 restart/reconnect binding follow-up is
positive single-controller evidence. It does not remove any requirement above.
No further identical preview game run is requested by this review.

See the [release checklist](directinput-release-checklist.md),
[installed acceptance steps](directinput-installed-acceptance.md) and
[identity audit](directinput-identity-audit.md). This review does not authorize
normal activation, driver installation, merge or release.
