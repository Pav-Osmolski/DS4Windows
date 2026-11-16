# DS5 Bridge provenance and attribution review

Reviewed 2026-11-16 for [discussion #126](https://github.com/Pav-Osmolski/DS4Windows/discussions/126),
reported by [up2urheadlights](https://github.com/up2urheadlights) on behalf of
[SundayMoments](https://github.com/SundayMoments).

## Scope and evidence

This review compares committed source and history. It does not infer copying
solely from a similar feature name or from the use of a documented Windows API.
The snapshots below demonstrate prior upstream implementations; they are not a
claim to know the exact DS5 Bridge checkout originally used for the port.

| DS4Windows code | Earlier DS5 Bridge source and observed correspondence |
| --- | --- |
| `DS4Control/AudioHapticsProcessor.cs` | [HostAudioHelper/Program.cs](https://github.com/SundayMoments/DS5_Bridge/blob/f0e3457f27a411e3af510b7b3879a9cb110c4c1d/companion/native/HostAudioHelper/Program.cs): matching gate threshold/rates, startup ramp, envelope processing, bass-focus gains, response gain/punch and rational soft clip. Later DS4Windows changes include sample-rate scaling and custom filters. |
| `DS4Control/ProfileFeatureSettings.cs` | [protocol.ts](https://github.com/SundayMoments/DS5_Bridge/blob/b5d87094c668cb369f8b9dc7a7ba8902150e9743/companion/src/shared/protocol.ts), renderer settings and Trigger Lab definitions: matching source/mode/bass/response/attack/release choices, app-session identity fields, trigger mode/start/wall/force settings and defaults. The inherited source explicitly acknowledged the DS5 Bridge feature contract. |
| `DS4Control/ProcessLoopbackAudioClient.cs` | [ProcessLoopbackAudioClient.cs](https://github.com/SundayMoments/DS5_Bridge/blob/f0e3457f27a411e3af510b7b3879a9cb110c4c1d/companion/native/HostAudioHelper/ProcessLoopbackAudioClient.cs): corresponding activation flow, PROPVARIANT/activation structs, completion handler, COM interfaces and marshaling. The Windows protocol itself is a documented public interface; this attribution concerns the adapted implementation. |
| `DS4Control/TriggerLabEffectEncoder.cs` | [bt.cpp](https://github.com/SundayMoments/DS5_Bridge/blob/f0e3457f27a411e3af510b7b3879a9cb110c4c1d/src/bt.cpp): matching strength `(percent * 8 + 99) / 100`, position `(percent + 5) / 10` capped at 9, and frequency `(percent * 28 + 50) / 100` mappings. The later DS4Windows encoder extracted this logic. |

The implementation and settings entered the inherited history through
[ab9aebe7](https://github.com/Pav-Osmolski/DS4Windows/commit/ab9aebe78a530871f393fee465d4e2c9f7a0a35e)
and [f33a79ca](https://github.com/Pav-Osmolski/DS4Windows/commit/f33a79ca1f9c4501c4ada54bec81c0a5fa9f314e)
("Add profile audio haptics and Trigger Lab", 2026-07-20).
Commit [cbb6e113](https://github.com/Pav-Osmolski/DS4Windows/commit/cbb6e113511b369da08266b546ebfffe703465c1)
(2026-07-31) removed the DS5 Bridge feature-contract reference from the settings
comment and the SundayMoments/DS5_Bridge adaptation reference from the artwork
notice. Both predate this fork's subsequent development.

## License and author attribution

The earlier DS5 Bridge [LICENSE](https://github.com/SundayMoments/DS5_Bridge/blob/b5d87094c668cb369f8b9dc7a7ba8902150e9743/LICENSE)
is the full GNU AGPL v3 text, and its
[companion package metadata](https://github.com/SundayMoments/DS5_Bridge/blob/b5d87094c668cb369f8b9dc7a7ba8902150e9743/companion/package.json)
explicitly identifies SundayMoments as author and `AGPL-3.0-only` as the license.
`AGPL-3.0.txt` is copied byte-for-byte from that committed LICENSE.

The compared upstream implementation files do not contain an individual
SundayMoments copyright header to reproduce verbatim. The restored notices
therefore explicitly credit SundayMoments for the DS5 Bridge-derived code,
retain the existing DS4Windows contributor copyright, and identify the
modifications and restoration date. They do not claim that this newly written
header was present verbatim upstream. The author can clarify preferred
copyright wording without changing the technical attribution.

The existing GPL text is retained. GPLv3 section 13 permits combination with
AGPLv3 code; AGPLv3 section 13 states that each part retains its applicable
license and specifies the network-source obligations for the combination.
The README, package metadata, notices and offline About page now identify both
licenses. This is not a claim that all dependencies or all historical files
have been relicensed as AGPL.

## Related notice checks

- Restored the removed DS5 Bridge artwork-adaptation credit. The original
  Gamepad Asset Pack copyright and MIT permission/warranty text remain intact.
- Checked the existing notices for HidLibrary, Crc32, SbcSharp, the Nielk1
  trigger-effect code, and the bundled VIIPER systray/WDL components. Their
  author/license notices remain present; they are not replaced by a blanket
  project license statement.
- Corrected the root NOTICE's stale VIIPER 0.1.11 paths to the bundled 0.1.17.
- The inherited cleanup also renamed transport/reference terminology involving
  other projects. A renamed symbol alone does not establish code derivation.
  This bounded review does not certify every historical transport, codec,
  dependency or asset. Additional specific provenance findings should extend
  this index and their applicable notices.

## Distribution checks

The project publishes COPYING, LICENSE.txt, NOTICE.txt, AGPL-3.0.txt and
DS5-BRIDGE-NOTICE.txt with both portable and installed applications. Package
composition and installer validation require them. The About license view
embeds the complete GPL/AGPL texts and DS5 Bridge notice, and Extras links to
DS5 Bridge. Both release preparation paths include the application notices in
the release-information archive; historical receipts remain verifiable.

Tests cover exact embedded text, missing package notices, release archive byte
preservation and rejection of missing notices. Corresponding-source archives
are already distributed beside binaries by the existing release process.
