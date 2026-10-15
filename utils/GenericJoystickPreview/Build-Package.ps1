param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ViiperRepository = (Join-Path $PSScriptRoot '../../../VIIPER'),
    [string]$Go = 'go.exe',
    [string]$DotNet = 'dotnet.exe',
    [string]$Python = 'python.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$viiper = (Resolve-Path $ViiperRepository).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
$null = New-Item -ItemType Directory -Path $output
function Check-Exit([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What failed ($LASTEXITCODE)." } }
Push-Location $viiper
try {
    & $Go build -o "$output/viiper.exe" ./cmd/viiper; Check-Exit 'Backend build'
    $viiperRevision = & git rev-parse HEAD; Check-Exit 'Backend revision'
    $viiperDirty = (@(& git status --porcelain).Count -gt 0)
} finally { Pop-Location }
& $DotNet publish "$repo/DS4Windows/DS4WinWPF.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $output
Check-Exit 'Portable application build'
& $Python "$PSScriptRoot/Prepare-Localization.py" $output
Check-Exit 'Preview localization composition'
foreach ($name in @('START.cmd', 'Run-Preview.ps1', 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output
}
Copy-Item -LiteralPath "$repo/utils/GenericJoystickLab/Lab.Helpers.ps1" -Destination $output
Push-Location $repo
try {
    $ds4Revision = & git rev-parse HEAD; Check-Exit 'Application revision'
    $ds4Dirty = (@(& git status --porcelain).Count -gt 0)
} finally { Pop-Location }
[string[]]$managedFiles = @(Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object {
    $_.FullName -ne (Join-Path $output '.ds4windows-managed-files.txt')
} | ForEach-Object { $_.FullName.Substring($output.Length + 1).Replace('\', '/') } | Sort-Object)
[IO.File]::WriteAllLines((Join-Path $output '.ds4windows-managed-files.txt'), $managedFiles, [Text.UTF8Encoding]::new($false))
[ordered]@{
    source = [ordered]@{ ds4windows = $ds4Revision; ds4windowsDirty = $ds4Dirty; viiper = $viiperRevision; viiperDirty = $viiperDirty }
    files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ name = $_.FullName.Substring($output.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/manifest.json" -Encoding UTF8
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$output/Run-Preview.ps1" -ValidateOnly
Check-Exit 'Package validation'
Compress-Archive -LiteralPath $output -DestinationPath "$output.zip"
Write-Host "Package: $output.zip"
