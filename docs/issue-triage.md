# Maintaining issue history

Classify the reported problem and its established outcome. Preserve original
reports and investigation comments; add a short clarification when later
evidence changes the interpretation.

| Label | Meaning |
| --- | --- |
| `bug` | A reported or established failure of expected application behaviour. An open investigation need not have a confirmed root cause. |
| `enhancement` | A new feature or improvement. |
| `usability` | Confusing controls, unclear status, readability or missing guidance. Can accompany a bug or enhancement. |
| `installation` | Installation, migration, upgrades, repairs or prerequisite cleanup. |
| `question` | Questions, troubleshooting and configuration support. |
| `accessibility` | Accessibility barriers being tracked deliberately; ordinary UI reports can use `usability`. |
| `stale` | Awaiting the requested evidence after troubleshooting guidance. Does not mean fixed. |
| `duplicate` | The same underlying report already tracked elsewhere, with an explicit canonical issue link. Similar symptoms alone are insufficient. |

Keep the closing reason and final comment accurate:

- A confirmed fix should name the published version and describe validation,
  including whether the reporter confirmed it.
- A configuration or migration resolution can be completed without claiming
  that an application bug was fixed.
- A report closed awaiting evidence should say it remains unconfirmed, retain
  the requested diagnostic information and explain when it can be reopened.
- Record follow-up functionality and its separate release when an issue spans
  more than one version. Link the relevant PRs and upstream reports explicitly.

Use cross-reference comments for related topics. GitHub's **Relationships**
controls represent blocking dependencies; use them only when one issue really
depends on another. Preserve established dependencies as historical context,
including after closure, but do not invent them to populate an empty panel.

Keep issues with different established causes separate. For example, migrated
slot settings, a missing active profile and an authenticated API connection
reset can all prevent usable output while requiring different resolutions.
