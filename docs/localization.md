# Languages and translation maintenance

The app ships 24 language packs plus English. Both `Translations/Strings.resx`
and the older `Properties/Resources.resx` string resources must be present in
every pack. Newer interface text uses the same resources, including setup,
profiles, battery alerts, audio and controller status. Product names, file
patterns, protocol identifiers and diagnostic field names remain technical
values rather than being translated.

Swedish uses `sv` and Indonesian uses `id`. Settings saved with the old `se`
or `idn` values migrate automatically. Language names use their native spelling
with an initial capital, preserving the remainder of the name. Language changes
require restarting the app; theme and accent changes do not.

## Coverage and wording

The October 2026 completion preserves suitable existing translations and fills
missing text with offline machine assistance. It also replaces obsolete
ViGEm/ScpDriver setup advice, Windows 7 driver instructions and misleading
compatibility promises. Historical resource identifiers remain so existing
callers continue working; the displayed instructions describe VIIPER and USB/IP.
References crediting ViGEm's historical contribution are retained.

The completion used the MIT-licensed Facebook M2M100 1.2B model through
CTranslate2. The converted model revision is
`081c9b82953d4eeb2309daabf2951e395c5dc979`; the base model/tokenizer revision is
`7b36184180524c1a1bbfa37f120a608046250b98`. The model and GPU libraries are
maintainer tools and are not distributed with or required by DS4Windows.
The process does not send app text or user information to a translation service.
Traditional Chinese machine additions are converted with OpenCC's Taiwan
traditional-character configuration.

Complete resource coverage means that every required entry is present. It does
not certify native-speaker fluency. Reports improving terminology, grammar or
context are welcome; include the language and exact screen or resource key.

## Checks for contributors

Run `python utils/validate-language-packs.py` before submitting translation work.
CI rejects missing/empty entries, duplicate keys, damaged numbered placeholders,
unresolved translation tokens and obsolete backend repair guidance. Compiled
tests check that every satellite contains both resource families, with no
neutral-language fallback, and that formatted strings can actually be rendered.

Use `utils/localization-interface-catalog.json` to find the source phrase and
screen for newer `UI_` keys. Their identities are the first 16 uppercase hex
digits of the source phrase's SHA-256 hash. Keep numbered placeholders such as
`{0}` and format suffixes intact; their order may change to suit the language.
Do not translate file paths, extensions, URLs or backend option names.

`utils/LocalizationTools` extracts C# UI text with Roslyn, while
`utils/extract-interface-strings.py` handles XAML attributes. Review the inventory
before applying extraction so protocol values never become translated labels.
`utils/complete-language-packs.py` can resume an offline completion from its local
cache and writes a provenance receipt. Existing human translations should be
preferred over regenerating a whole pack. Review any machine additions in context.
