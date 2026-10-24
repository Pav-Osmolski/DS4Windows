# Issue 66 connection reliability candidate

This testing-only candidate is based on stable DS4Windows 5.0.46 and VIIPER
0.1.13. It retains the current features and translations while porting only
the connection fixes from the diagnostic preview. Thanks to
[@cns00](https://github.com/cns00) for the comparisons and installer follow-up
in [issue 66](https://github.com/Pav-Osmolski/DS4Windows/issues/66).

## Changes

- VIIPER gracefully finishes complete one-shot management replies with a
  250 ms / 4097-byte bound; server shutdown can interrupt the registered socket.
  Controller/audio streams, authentication, framing and operation retries are
  unchanged. Source: [backend PR 9](https://github.com/Pav-Osmolski/VIIPER/pull/9).
- App and installer readiness probes use cancellable TCP connection deadlines
  instead of borrowing the legacy socket completion wait handle.
- No diagnostic collector, independent probe, ping-only server bypass or
  original/graceful comparison option ships in this candidate.

## Reporter evidence

Five captures used original/graceful/graceful/original ordering on the same
isolated port with both DS4Windows and independent Go clients. They created
no controllers and attached no USB devices. Original closing failed 77/400
requests versus 3/400 with graceful closing. Live preview-server checks failed
5/400. A controller startup reset recovered using the existing single retry.
These observations support improved reliability, not complete resolution or
an identified hardware defect, Windows-version issue or security-product cause.

The 5.0.46 installer follow-up shows successful helper execution and verified
USB/IP/VIIPER readiness, followed by a failed separate final health check. The
reporter says Retry succeeded. The installer uses the same legacy wait-handle
pattern as the application probe; this is corrected in both places. The supplied
evidence does not distinguish that race from a transient connection reset.

## Validation and acceptance

The real app and installer probes each pass 256 rapid loopback connections.
Existing reply tests retain encrypted-record validation, completed-reply/reset,
fragmentation and deadline coverage. Backend tests cover FIN delivery, bounded
silent/excessive peers, server shutdown during drain and 200 authenticated
requests plus API-error response cleanup. Full app, standalone and backend
Release suites, package identities, source hashes and language loading are
checked for the final candidate. Hardware tests remain explicitly opt-in.

Portable and installer downloads are testing-only and unsigned. MSI composition,
offline payload/manifest validation and installer state-machine simulations are
required before upload; full stable installer lifecycle/upgrade gates remain
required before an eventual stable release. No installation is performed on
the maintainer's working computer as part of automated checks.

Stable 5.0.46 remains Latest. Maintainer and reporter acceptance are pending;
do not close issue 66 or claim every reset is fixed on the basis of these checks.

## Acceptance — 24 October 2026

The reporter tested the final 5.0.47 paired pre-release and confirmed consistently
reaching Ready. The maintainer authorized merging and publishing the normal
5.0.47 release with VIIPER 0.1.14, including the installer readiness correction.
The preceding candidate status is the historical test-stage record. The released
fix resolves the reported behaviour; it does not establish a hardware fault,
identify the host-specific underlying trigger, or eliminate all possible resets.
