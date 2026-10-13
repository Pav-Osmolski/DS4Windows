# Development

[Documentation index](README.md) · [Contributing](../contributing.md)

## Build on Windows

Use Windows x64, Git and the **exact SDK declared in `global.json`** (currently
8.0.425). Automatic roll-forward and prerelease selection are disabled. Install
that SDK before running the commands below; a different .NET 8 SDK is not an
interchangeable build input. Python 3.10 or newer is used by packaging scripts.
The current VIIPER distribution targets Windows x64 even though legacy x86
project configurations remain in the solution.

From the repository root:

```powershell
dotnet restore .\DS4WindowsTests\DS4WindowsTests.csproj -p:Platform=x64 --locked-mode
dotnet build .\DS4Windows\DS4WinWPF.csproj -c Release -p:Platform=x64 --no-restore
dotnet test .\DS4WindowsTests\DS4WindowsTests.csproj -c Release -p:Platform=x64 --no-restore --logger trx --results-directory TestResults/main --blame-hang-timeout 5m

dotnet restore .\utils\Switch2UsbHardwareVerify.Tests\Switch2UsbHardwareVerify.Tests.csproj --locked-mode
dotnet test .\utils\Switch2UsbHardwareVerify.Tests\Switch2UsbHardwareVerify.Tests.csproj -c Release --no-restore --logger trx --results-directory TestResults/standalone --blame-hang-timeout 5m
```

The main test project's restore includes its application reference. The standalone
hardware-verifier project has its own unit-test suite; running it does not connect
to or qualify hardware. Explicit hardware opt-in tests remain separate. For a
focused main-suite run, add `--filter` with the relevant test class/method; use
both complete suites before release qualification.

Committed `packages.lock.json` files make dependency changes reviewable. CI uses
locked restore. For ordinary local validation, use `--locked-mode` too. An
intentional package/SDK change requires refreshing and reviewing the affected
locks as described in [Dependency maintenance](dependency-maintenance.md#sdk-and-dependency-locks);
do not bypass a lock mismatch to make CI pass.

The WPF application project is `DS4Windows/DS4WinWPF.csproj`. Open
`DS4WindowsWPF.sln` for Visual Studio development and install a compatible Windows
SDK for the project's target. UI regression tests use a shared process-lifetime
STA dispatcher; follow the existing fixture helpers when adding WPF tests instead
of creating competing application dispatchers.

Building does not install drivers or establish controller compatibility. Use a
supported matching package for hardware testing.

## Publish and package

Restore the application's declared runtime graph without overriding it with `-r`,
then publish x64 with `--no-restore`. This preserves the reviewed x64/x86 lock
graph instead of rewriting it to a single runtime identifier.

```powershell
dotnet restore .\DS4Windows\DS4WinWPF.csproj -p:Platform=x64 -p:SelfContained=true --locked-mode
dotnet publish .\DS4Windows\DS4WinWPF.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true --no-restore -o .\bin\x64\Release\output

# Local package label only; this command does not create an official release.
$localPackageVersion = "local-dev"
python .\utils\post-build.py .\bin\x64\Release\output . $localPackageVersion
```

The final argument is a package label, not an instruction to change the compiled
app version. Official releases coordinate package, app/file and installer versions
through the [release process](release-process.md).

The post-build script requires the complete offline payload, adds the portable
backend and marker, and places satellite assemblies under `Lang/<culture>`.
It creates the staged `bin/x64/Release/DS4Windows` directory and ZIP. Do not
publish a partial build directory or manually repack selected files. See
[Translations](dev/translations.md) for resource-loading checks and
[satellite-resource probe](../utils/SatelliteResourceProbe/README.md) for packaged
resource checks.

Installer authoring and validation are documented in
[installer/README.md](../installer/README.md). Run the checks appropriate to
packaging/installer changes, including integrity, install/repair/uninstall and
prior-version upgrade verification. A successful local ZIP is not a substitute
for the exact-source release pipeline.

## Hosted builds and test evidence

CI tests branch revisions and proposed PR merges. When a branch has an open PR,
its downloadable package normally comes from the proposed merge, avoiding a
second package build for the same contribution. Branches without PRs retain
package builds. Use the PR run's package to test the proposed merged source;
check the run revision rather than assuming every artifact belongs to the branch
head. The [build/test audit](build-test-audit.md) records the policy and tradeoffs.

NuGet caching, removal of redundant restores and cancellation of superseded CI
runs reduce repeated work. Test reports and hang diagnostics are retained. The
release pipeline runs both suites against the exact source before packaging and
keeps translation and installer checks. Installer-stage timing diagnostics help
locate slow phases without removing validation. Do not use an obsolete run's
success as evidence for a newer revision.

## Code and evidence

| Location | Contents |
| --- | --- |
| `DS4Windows/DS4Forms` | WPF views and handlers |
| `DS4Windows/DS4Control` | Controller, mapping and settings services |
| `DS4WindowsTests` | Automated regression tests |
| `utils` | Packaging, validation and diagnostic tools |
| `installer` | Installer projects and scripts |
| `docs/protocols` | Runtime and transport contracts |
| `docs/validation` | Dated investigations and test records |

CI runs the full x64 suite and packaging/installer checks. Run focused tests
while developing, then the checks relevant to your change before proposing it.
Document opt-in hardware tests and skipped checks explicitly. Avoid rewriting
older evidence to make a newer result look like it was tested historically.

## Documentation changes

Keep README concise, user instructions in `docs`, and release history in
CHANGELOG.md. Update links after moves and remove redundant pointer files.
Preserve substantive historical records, third-party notices and contributor attribution.
