param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ViiperRepository = (Join-Path $PSScriptRoot '../../../VIIPER'),
    [string]$Go = 'go.exe',
    [string]$DotNet = 'dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$viiper = (Resolve-Path $ViiperRepository).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory; existing packages are never overwritten.' }
$null = New-Item -ItemType Directory -Path $output
function Check-Exit([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What failed ($LASTEXITCODE)." } }
foreach ($architecture in @('x64', 'x86')) {
    & "$repo/utils/GenericJoystickProbe/build.ps1" -Architecture $architecture -OutputDirectory "$output-native"
    Copy-Item -LiteralPath "$output-native/GenericJoystickProbe-$architecture.exe" -Destination $output
    & "$output/GenericJoystickProbe-$architecture.exe" --self-test; Check-Exit "Probe $architecture conversion tests"
}
Push-Location $viiper
try {
    & $Go build -o "$output/GenericJoystickLabClient.exe" ./examples/genericjoystick-lab; Check-Exit 'Lab client build'
    & $Go build -o "$output/viiper-directinput-lab.exe" ./cmd/viiper; Check-Exit 'Lab backend build'
    $viiperRevision = & git rev-parse HEAD; Check-Exit 'VIIPER revision'
    $viiperDirty = (@(& git status --porcelain).Count -gt 0)
} finally { Pop-Location }
$generator = "$output-generator"
& $DotNet publish "$repo/utils/GenericJoystickLabPlans/GenericJoystickLabPlans.csproj" -c Release -r win-x64 --self-contained true -o $generator
Check-Exit 'Mapping fixture build'
& "$generator/GenericJoystickLabPlans.exe" "$output/plans.json"; Check-Exit 'Mapping fixture generation'
foreach ($name in @('START.cmd', 'Run-Lab.ps1', 'Lab.Helpers.ps1', 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output
}
Push-Location $repo
try {
    $ds4Revision = & git rev-parse HEAD; Check-Exit 'DS4Windows revision'
    $ds4Dirty = (@(& git status --porcelain).Count -gt 0)
} finally { Pop-Location }
$manifest = [ordered]@{
    source = [ordered]@{ ds4windows = $ds4Revision; ds4windowsDirty = $ds4Dirty; viiper = $viiperRevision; viiperDirty = $viiperDirty }
    files = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/manifest.json" -Encoding UTF8
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$output/Run-Lab.ps1" -ValidateOnly; Check-Exit 'Package validation'
Compress-Archive -LiteralPath $output -DestinationPath "$output.zip"
Write-Host "Package: $output.zip"
