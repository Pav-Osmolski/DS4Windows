param(
    [Parameter(Mandatory = $true)][string]$SourceBuild,
    [switch]$StandardLayout
)

$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceBuild).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$probeBuild = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows'
$fixtureRoot = Join-Path $repositoryRoot ('artifacts/issue60-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $fixtureRoot 'package'
$outsideRoot = Join-Path $fixtureRoot 'outside'
New-Item -ItemType Directory -Path $packageRoot, $outsideRoot | Out-Null

$sourceRuntime = Join-Path $sourceRoot 'Runtime'
$splitLayout = Test-Path -LiteralPath (Join-Path $sourceRuntime 'DS4Windows.dll') -PathType Leaf
$packageRuntime = $packageRoot
if ($splitLayout) {
    $packageRuntime = Join-Path $packageRoot 'Runtime'
    New-Item -ItemType Directory -Path $packageRuntime | Out-Null
    Copy-Item -LiteralPath (Join-Path $probeBuild 'SplitSatelliteResourceProbe.exe') -Destination $packageRoot
}
else {
    $sourceRuntime = $sourceRoot
}

# Preserve the real application's resource/dependency identities, but omit
# unrelated libraries: this child only requests resources, never runs the app.
$sourceDeps = Get-Content -LiteralPath (Join-Path $sourceRuntime 'DS4Windows.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$sourceTarget = $sourceDeps.targets[$sourceDeps.runtimeTarget.name]
$probeDeps = Get-Content -LiteralPath (Join-Path $probeBuild 'SatelliteResourceProbe.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$probeTarget = $probeDeps.targets[$probeDeps.runtimeTarget.name]
foreach ($libraryName in $sourceTarget.Keys) {
    if ($libraryName -notlike 'DS4Windows/*' -and $libraryName -notlike 'TaskScheduler/*') { continue }
    $library = $sourceTarget[$libraryName]
    $probeTarget[$libraryName] = @{ runtime = $library.runtime; resources = $library.resources }
    $probeDeps.libraries[$libraryName] = $sourceDeps.libraries[$libraryName]
    if ($libraryName -like 'DS4Windows/*') {
        $probeDeps.libraries[$libraryName].path = './'
    }

    foreach ($asset in $library.runtime.Keys) {
        $leaf = Split-Path -Leaf $asset
        Copy-Item -LiteralPath (Join-Path $sourceRuntime $leaf) -Destination (Join-Path $packageRuntime $leaf)
    }
    foreach ($asset in $library.resources.Keys) {
        $culture = $library.resources[$asset].locale
        $leaf = Split-Path -Leaf $asset
        $sourceSatellite = Join-Path $sourceRoot (Join-Path $culture $leaf)
        if (-not (Test-Path -LiteralPath $sourceSatellite -PathType Leaf)) {
            $sourceSatellite = Join-Path $sourceRoot (Join-Path 'Lang' (Join-Path $culture $leaf))
        }
        $destination = if ($StandardLayout) { Join-Path $packageRuntime $culture } else { Join-Path $packageRoot (Join-Path 'Lang' $culture) }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath $sourceSatellite -Destination (Join-Path $destination $leaf)
    }
}

Copy-Item -LiteralPath (Join-Path $probeBuild 'SatelliteResourceProbe.dll') -Destination $packageRuntime
Copy-Item -LiteralPath (Join-Path $probeBuild 'SatelliteResourceProbe.runtimeconfig.json') -Destination $packageRuntime
$probeDeps | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath (Join-Path $packageRuntime 'SatelliteResourceProbe.deps.json') -Encoding utf8

$resultCount = 0
foreach ($startDirectory in @($packageRoot, $outsideRoot)) {
    Push-Location -LiteralPath $startDirectory
    try {
        $probeOutput = if ($splitLayout) {
            & (Join-Path $packageRoot 'SplitSatelliteResourceProbe.exe')
        }
        else {
            & dotnet (Join-Path $packageRuntime 'SatelliteResourceProbe.dll')
        }
        $probeExit = $LASTEXITCODE
        $probeOutput
        $result = $probeOutput | ConvertFrom-Json
        $shouldLocalize = $true
        if ($result.Localized -ne $shouldLocalize -or
            ($null -ne $result.SchedulerSatellite) -ne $shouldLocalize -or
            -not $result.UnrelatedRejected -or
            $result.RegionalGerman -ne $result.German -or
            $result.UnavailableCulture -ne $result.Neutral -or
            $probeExit -ne $(if ($shouldLocalize) { 0 } else { 1 })) {
            throw 'Resource loading did not match the expected isolated layout behavior.'
        }
        $resultCount++
    }
    finally { Pop-Location }
}
Write-Output "Verified $resultCount isolated resource-loading cases. Fixture: $fixtureRoot"
