[CmdletBinding()]
param([Parameter(Mandatory)][string]$PreviewRoot)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'Elevated collector verification is restricted to a disposable hosted runner.'
}
$PreviewRoot = (Resolve-Path -LiteralPath $PreviewRoot).Path
$collectorHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PreviewRoot 'extras\Collect-Issue66.ps1')).Hash
$assemblyHex = [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $PreviewRoot 'DS4Windows.dll')))
$pinHex = [Convert]::ToHexString([Text.Encoding]::Unicode.GetBytes($collectorHash))
if (-not $assemblyHex.Contains($pinHex)) { throw 'Published collector bytes do not match the app embedded pin.' }
$logs = Join-Path $env:RUNNER_TEMP ('issue66-empty-logs-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $logs)
# post-build puts the aliases in the ZIP only; materialize only these exact bytes.
$alias = Join-Path $PreviewRoot 'viiper.exe'
if (Test-Path -LiteralPath $alias) { throw 'Expected a fresh unpackaged publish tree.' }
[IO.File]::Copy((Join-Path $PreviewRoot 'extras\VIIPER-0.1.12-x64.exe'),$alias,$false)
try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PreviewRoot 'extras\Collect-Issue66.ps1') `
        -LogDirectory $logs -LiveKeyPath (Join-Path $logs 'absent-key.txt') -SkipLiveTests -NonInteractive -ManualSeconds 0
    if ($LASTEXITCODE -ne 0) { throw 'Collector process failed.' }
    $capture = @(Get-ChildItem -LiteralPath (Join-Path $PreviewRoot 'Diagnostics') -Directory)
    if ($capture.Count -ne 1) { throw 'Expected exactly one fresh diagnostic session.' }
    $evidence = Join-Path $capture[0].FullName 'evidence'
    $completion = Get-Content -LiteralPath (Join-Path $evidence 'completion.json') -Raw | ConvertFrom-Json
    Write-Host (Get-Content -LiteralPath (Join-Path $evidence 'completion.json') -Raw)
    foreach ($name in @('trace-start.txt','trace-start-fallback.txt')) {
        $path = Join-Path $evidence $name
        if (Test-Path -LiteralPath $path) { Write-Host ($name + ': ' + (Get-Content -LiteralPath $path -Raw)) }
    }
    if (-not $completion.traceStarted -or $completion.phaseCount -ne 4) { throw 'Missing Windows trace or comparison phases.' }
    foreach ($phase in @('1-immediate','2-graceful','3-graceful','4-immediate')) {
        $dotnet = @(Get-Content -LiteralPath (Join-Path $evidence ($phase + '-dotnet.json')) -Raw | ConvertFrom-Json)
        $go = Get-Content -LiteralPath (Join-Path $evidence ($phase + '-go.json')) -Raw | ConvertFrom-Json
        if ($dotnet.Count -ne 20 -or @($dotnet | Where-Object {-not $_.success}).Count -gt 0 -or
            $go.setupFailed -or $go.successful -ne 20 -or $go.attempted -ne 20) { throw "Connection comparison failed: $phase" }
        $server = Get-Content -LiteralPath (Join-Path $evidence ($phase + '-server.log')) -Raw
        if ($phase -match 'graceful' -and $server -notmatch 'api send shutdown') { throw 'Graceful path was not exercised.' }
        if ($phase -match 'immediate' -and $server -match 'api send shutdown') { throw 'Original path unexpectedly half-closed.' }
    }
    if ((Get-Item -LiteralPath (Join-Path $evidence 'tcpip.etl')).Length -le 4096) { throw 'Windows trace is empty.' }
    if ((Get-Content -LiteralPath (Join-Path $evidence 'trace-status.txt') -Raw) -notmatch '2F07E2EE|TCPIP') { throw 'TCPIP provider not enabled.' }
    $reset = Get-Content -LiteralPath (Join-Path $evidence 'controlled-reset-self-test.json') -Raw | ConvertFrom-Json
    if ($reset.observedError -ne 10054) { throw 'Controlled reset self-test did not trigger the expected socket error.' }
    $decoded = @(Get-Content -LiteralPath (Join-Path $evidence 'tcp-reset-events.json') -Raw | ConvertFrom-Json)
    $resetSignature = '0200' + ('{0:X4}' -f [int]$reset.serverPort) + '7F000001'
    if (@($decoded | Where-Object {$_.xml.Contains($resetSignature)}).Count -lt 1) { throw 'The controlled reset was not correlated to its exact loopback endpoint in Windows events.' }
    $key = Join-Path $capture[0].FullName 'private\lab-data\viiper.key.txt'
    if (Test-Path -LiteralPath $key) { throw 'Temporary credential was retained.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = @(Get-ChildItem -LiteralPath (Join-Path $PreviewRoot 'Diagnostics') -Filter '*.zip')
    $archive = [IO.Compression.ZipFile]::OpenRead($zip[0].FullName)
    try {
        if (@($archive.Entries | Where-Object { $_.FullName -match 'key\.txt|(^|/)private/' }).Count -gt 0) { throw 'Bundle contains private work files.' }
    } finally { $archive.Dispose() }
    Write-Host 'PASS: all 160 pings, both closing modes, Windows TCP trace and credential exclusion.'
} finally { if (Test-Path -LiteralPath $alias) { Remove-Item -LiteralPath $alias } }
