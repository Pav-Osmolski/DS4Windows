param([string]$WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/release.yml'))
$ErrorActionPreference = 'Stop'
$workflow = Get-Content -LiteralPath $WorkflowPath -Raw
# Exercise the actual archive creation and publication checks without GitHub,
# signing, or modifying any existing release files.
function Read-Block([string]$start, [string]$end, [int]$after = 0) {
    $first = $workflow.IndexOf($start, $after, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Missing workflow block: $start" }
    $last = $workflow.IndexOf($end, $first, [StringComparison]::Ordinal)
    if ($last -lt 0) { throw "Missing workflow boundary: $end" }
    return [scriptblock]::Create(($workflow.Substring($first, $last - $first) -split '\r?\n' |
        ForEach-Object { $_ -replace '^        ', '' }) -join "`n")
}
$creation = Read-Block '$noticeNames = @(' '$assets = @('
$verification = Read-Block '$noticeNames = @(' "Write-Host 'Published release matches" ($workflow.IndexOf('  verify_published_release:'))
$root = Join-Path ([IO.Path]::GetTempPath()) "ds4w-release-info-$([guid]::NewGuid().ToString('N'))"
$records = Join-Path $root 'records'
$publish = Join-Path $root 'publish'
$oldEnvironment = @{}
foreach ($name in @('VERSION', 'RELEASE_TAG', 'UNSIGNED_RELEASE')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    New-Item -ItemType Directory -Path $records, (Join-Path $publish 'extras') | Out-Null
    $names = @('VIIPER-0.1.12-LICENSES.txt', 'VIIPER-0.1.12-PROVENANCE.txt',
        'VIIPER-0.1.12-BUILD-NOTES.txt', 'VIIPER-SYSTRAY-LICENSE.txt', 'VIIPER-SYSTRAY-NOTICE.md',
        'VIIPER-WDL-NOTICE.txt', 'LICENSE.txt')
    foreach ($name in $names) { [IO.File]::WriteAllText((Join-Path $publish "extras/$name"), "Fixture $name`r`nUnicode: ©`r`n") }
    $env:VERSION = $env:RELEASE_TAG = '5.0.28'
    $env:UNSIGNED_RELEASE = 'true'
    $sourceCommit = 'a' * 40; $brokerCommit = 'b' * 40; $brokerHash = 'C' * 64
    . $creation
    $receipt = @{ releaseInfo = $releaseInfo }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @($infoName, 'installer.exe', 'portable.zip', 'ds4-source.zip', 'viiper-source.zip', 'SHA256SUMS.txt')) { [void]$seen.Add($name) }
    $release = @{ assets = @(1..7) }
    . $verification
    # Seven top-level assets: binaries, source ZIPs, information ZIP, sums, receipt.
    if (@(Get-ChildItem -LiteralPath $records -File).Count -ne 1) { throw 'Loose supporting files escaped the information directory.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($infoPath)
    try {
        foreach ($entry in $zip.Entries) {
            $original = Join-Path $infoDirectory $entry.FullName
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -cne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash) { throw 'Archive changed original bytes.' }
        }
    } finally { $zip.Dispose() }
    $baseline = [IO.File]::ReadAllBytes($infoPath)
    foreach ($case in @('missing', 'extra', 'duplicate', 'changed', 'unsafe-name', 'wrong-receipt-hash', 'wrong-asset-count', 'unrecorded-asset')) {
        [IO.File]::WriteAllBytes($infoPath, $baseline)
        $receipt = @{ releaseInfo = ($releaseInfo | ConvertTo-Json -Depth 5 | ConvertFrom-Json) }
        if ($case -eq 'wrong-receipt-hash') { $receipt.releaseInfo.files[0].sha256 = '0' * 64 }
        elseif ($case -eq 'unrecorded-asset') { $release.assets = @(1..8) }
        elseif ($case -eq 'wrong-asset-count') { [void]$seen.Add('unexpected.txt') }
        else {
            $zip = [IO.Compression.ZipFile]::Open($infoPath, [IO.Compression.ZipArchiveMode]::Update)
            try {
                if ($case -eq 'missing') { $zip.GetEntry('LICENSE.txt').Delete() }
                elseif ($case -eq 'unsafe-name') { $zip.GetEntry('LICENSE.txt').Delete(); [void]$zip.CreateEntry('../LICENSE.txt') }
                elseif ($case -eq 'changed') {
                    $zip.GetEntry('LICENSE.txt').Delete()
                    $writer = [IO.StreamWriter]::new($zip.CreateEntry('LICENSE.txt').Open())
                    try { $writer.Write('changed') } finally { $writer.Dispose() }
                } else { [void]$zip.CreateEntry($(if ($case -eq 'duplicate') { 'LICENSE.txt' } else { 'unexpected.txt' })) }
            } finally { $zip.Dispose() }
        }
        $rejected = $false
        try { . $verification } catch { $rejected = $true }
        if (-not $rejected) { throw "Publication accepted invalid information: $case" }
        [void]$seen.Remove('unexpected.txt')
        $release.assets = @(1..7)
    }
    # An old receipt still verifies its original standalone supporting files.
    $receipt = @{}
    foreach ($name in $names + @('SOURCE-REVISIONS.txt')) { [void]$seen.Add($name) }
    . $verification
    [void]$seen.Remove('LICENSE.txt')
    $rejected = $false
    try { . $verification } catch { $rejected = $true }
    if (-not $rejected) { throw 'Incomplete historical release was accepted.' }
    Write-Host 'Release information: byte preservation, eight rejection cases and historical compatibility passed.'
} finally {
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
    # The only removed directory is the dedicated absolute temporary fixture.
    if ([IO.Path]::GetDirectoryName($root) -cne [IO.Path]::GetTempPath().TrimEnd('\', '/')) { throw 'Unexpected fixture path.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
