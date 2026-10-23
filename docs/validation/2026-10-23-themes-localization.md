# Themes and language audit — 23 October 2026

Implemented on `feature/themes-localization`, based on main commit
`986fbef6e669c116d985da2cdf0dfa6bb9bccf5a`.

## Outcome

- Added Graphite, Paper, Midnight and High contrast, retaining Light and Dark.
  Follow Windows replaces Default. Palette previews, preset accents and a custom
  colour picker apply immediately and persist in app settings.
- Windows contrast preferences take priority and retain the system palette.
  Normal palettes adjust accent and secondary text for at least 4.5:1 contrast
  against page, card and raised surfaces. Accent fills use contrasting text.
- Extracted newer XAML and C# interface text into resources, completing both
  resource families in all 24 supported languages. Product names, protocol
  identifiers, URLs and file patterns remain technical values.
- Migrated Swedish `se` to `sv` and Indonesian `idn` to `id`; standardized the
  first letter of native language names without rewriting the remainder.
- Corrected obsolete backend repair instructions, Windows 7 driver advice and
  unsupported compatibility promises. Fixed unresolved preset, precision,
  dead-zone and DS3 tooltip resource references.
- Corrected EXE company, authors, description and repository metadata for the
  Pav-Osmolski fork, retaining historical copyright credits.

Existing suitable translations were retained. Missing text uses offline M2M100
assistance, technical-token preservation and supplied terminology corrections
across all 24 languages. Theme names and common audio, battery and rule labels
have additional supplied translations. These additions have not had independent
native-speaker review in every language. Complete coverage measures presence
and formatting safety, not fluency. See [translation maintenance](../localization.md).

## Validation

- Full application suite: **7,759 passed, 16 skipped, zero failures**.
- Separate verification utility suite: **75 passed**.
- Final theme, translation and EXE metadata checks: **six passed**, after the
  final palette changes and terminology corrections.
- All **48 language/resource-family pairs** pass coverage, empty-value,
  duplicate-key, placeholder and current repair-advice checks. Compiled tests
  inspect actual satellite resource sets without parent-language fallback and
  exercise formatted translations.
- Packaging layout suite: 34 tests, one skipped, zero failures.
- Text-encoding checks and whitespace checks pass.
- Rendered all six explicit palettes and actual Polish, Arabic and Japanese
  battery screens. Checked theme/accent persistence, repeated custom accent
  selection and Windows contrast priority.

Localized WPF tests now share the existing process-lifetime dispatcher;
temporary test threads cannot strand the localization provider. Source-layout
tests resolve resource text before isolated parsing and retain wording checks.

Physical controller behavior was not changed. Hardware-only checks remain
opt-in. The local preview is separate from a public release.

## Coverage comparison

The older Properties resource family previously had only three translated packs.
Every current language now contains both complete resource sets.

| Culture | Previous primary | Current primary | Current Properties |
| --- | ---: | ---: | ---: |
| ar | 105/446 | 2073/2073 | 180/180 |
| cs | 115/446 | 2073/2073 | 180/180 |
| de | 175/446 | 2073/2073 | 180/180 |
| el | 217/446 | 2073/2073 | 180/180 |
| es | 105/446 | 2073/2073 | 180/180 |
| fi | 199/446 | 2073/2073 | 180/180 |
| fr | 200/446 | 2073/2073 | 180/180 |
| he | 105/446 | 2073/2073 | 180/180 |
| hu-HU | 105/446 | 2073/2073 | 180/180 |
| id | 217/446 | 2073/2073 | 180/180 |
| it | 193/446 | 2073/2073 | 180/180 |
| ja | 115/446 | 2073/2073 | 180/180 |
| ms | 221/446 | 2073/2073 | 180/180 |
| nl | 414/446 | 2073/2073 | 180/180 |
| pl | 106/446 | 2073/2073 | 180/180 |
| pt | 109/446 | 2073/2073 | 180/180 |
| pt-BR | 105/446 | 2073/2073 | 180/180 |
| ru | 375/446 | 2073/2073 | 180/180 |
| sv | 141/446 | 2073/2073 | 180/180 |
| tr | 221/446 | 2073/2073 | 180/180 |
| uk-UA | 105/446 | 2073/2073 | 180/180 |
| vi | 200/446 | 2073/2073 | 180/180 |
| zh-Hans | 399/446 | 2073/2073 | 180/180 |
| zh-Hant | 141/446 | 2073/2073 | 180/180 |
