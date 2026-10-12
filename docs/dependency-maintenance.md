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

## Coverage and review priorities

The NuGet configuration covers the application's packages, both test projects,
the C# installer helpers and standalone C# tools. Individual packages do not
need separate configuration entries. GitHub Actions are monitored independently.
Dependabot proposes changes; passing CI does not automatically merge them.

The installer scan intentionally names the C# helper directories. Dependabot's
current [NuGet discovery implementation](https://github.com/dependabot/dependabot-core/blob/main/nuget/helpers/lib/NuGetUpdater/NuGetUpdater.Core/Discover/DiscoveryWorker.cs)
does not recognise standalone `.wixproj` entry points. Review `WixToolset.Sdk`
and `WixToolset.Util.wixext` manually alongside any proposed
`WixToolset.BootstrapperApplicationApi` update. Keep that installer family
compatible and run the full installer lifecycle checks.

Recommended order after accepting the initial dependency refresh:

| Packages | Review focus |
| --- | --- |
| WpfScreenHelper; Microsoft.Diagnostics.Tracing.TraceEvent | Small patch updates; monitor selection and diagnostic-tool builds |
| TaskScheduler | Startup task creation, recovery, ownership and durable backups |
| H.NotifyIcon.Wpf; DotNetProjects.Extended.Wpf.Toolkit; WPFLocalizeExtension | Tray behaviour, dark/light themes, dropdowns, packaged translations and visual regression checks |
| Microsoft.Windows.CsWin32 | Generated native declarations, structure layouts and Windows interop; a 0.x update can still break compatibility |
| coverlet.collector | Coverage discovery and non-empty reports using debug symbols; keep separate from application runtime changes |
| NAudio; NLog; WiX | Deliberate major-version migrations in separate PRs, with audio hardware, logging/export and installer checks respectively |

As of **12 October 2026**, NuGet lists patch updates for WpfScreenHelper
(2.1.0 to [2.1.1](https://www.nuget.org/packages/WpfScreenHelper/2.1.1)) and
TraceEvent (3.2.6 to [3.2.8](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.2.8)).
Later versions also exist for the other packages in the table. These are review
candidates, not an instruction to take the newest version without checking it.
BouncyCastle.Cryptography, Concentus, MdXaml, Ookii.Dialogs.Wpf,
YellowDogMan.RRNoise.NET and bloomtom.HttpProgress were already at their latest
stable NuGet versions during this audit; continue monitoring them.

Keep Actions major-version updates separate from ordinary patch/minor batches,
and verify their runtime requirements and the full workflow. Preserve workflow
permissions, signing policy and secret handling unless a reviewed change needs
them. The existing .NET 8 workflow selector follows SDK patches; a framework
major-version migration is a separate compatibility project. There is currently
no `global.json` to monitor through the `dotnet-sdk` ecosystem, and no npm,
Python, Go or submodule manifests in this repository that justify additional
Dependabot ecosystems.

## Components requiring manual maintenance

Bundled VIIPER and DS4Updater executables, the usbip-win2 installer, HidHide,
FakerInput and raw native DLLs are not ordinary NuGet version references.
Update them through their documented source/build and packaging process,
including provenance, hashes and compatibility checks. Coordinate VIIPER and
USB/IP changes across their repositories rather than letting an unrelated
package update replace driver binaries.

RRNoise is a useful exception: its NuGet reference is monitored, but the app
copies a native DLL from that package. Review native architecture, shipped
payload and audio behaviour even when Dependabot supplies the version bump.
