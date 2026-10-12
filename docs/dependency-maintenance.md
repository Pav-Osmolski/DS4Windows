# Dependency maintenance

Dependabot checks NuGet projects and GitHub Actions every Monday at 07:00
Europe/London. Version-update PRs are limited to three for NuGet and two for
Actions. Related MSTest/test-runner, Windows-management and WiX dependencies
are grouped to reduce mismatched versions. Actions patch/minor updates are
grouped; major Actions changes remain separate. These groups do not imply
compatibility or approval: major NuGet upgrades still need deliberate review.

No automatic merging is configured. Preserve the full test, package and installer
checks. Review runtime changes with driver detection, controller reconnection
and suspend/resume testing; review installer dependency changes with both app
and installer helper builds. Review test-tool upgrades separately, preserving
exact exception assertions, test discovery, coverage and explicit hardware skips.

## Fork activation

A configuration file alone does not enable version updates in a fork. After
merging this configuration, check **Settings > Advanced Security > Dependabot
version updates** and enable it if disabled. Confirm the monitored ecosystems
and update jobs under **Insights > Dependency graph > Dependabot**. Version
updates and vulnerability-driven security updates have separate settings.

See [GitHub's fork instructions](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configure-version-updates#enabling-version-updates-on-forks).

## Existing inherited branches

The four inherited June dependency branches are historical proposals, not tested
updates for this fork. Replace their intent with current-main PRs; the separate
MSTest-framework proposal overlaps the adapter/framework proposal. Preserve the
old references until replacements are accepted. Branch cleanup is a separate
maintainer decision; preserve published release tags.

A removed direct NuGet reference can still appear transitively in the dependency
graph. Confirm restored assets and published outputs before claiming a dependency
was removed from the shipped application. Scan direct and transitive dependencies
for advisories when reviewing updates; an outdated version is not by itself proof
of a known vulnerability.
