[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EventName,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$RefName,
    [Parameter(Mandatory)][string]$DefaultBranch,
    [ValidateSet('none','portable','installer')][string]$ManualPackage = 'portable',
    [string[]]$ChangedFiles,
    [string]$BaseCommit,
    [string]$OutputPath = $env:GITHUB_OUTPUT,
    [scriptblock]$PullRequestLookup
)
$ErrorActionPreference = 'Stop'
$test = $true
$package = 'none'
$reason = 'Validate development source; request preview packages explicitly.'
if (-not $PSBoundParameters.ContainsKey('ChangedFiles') -and $BaseCommit -match '^[0-9a-f]{40}$' -and $BaseCommit -ne ('0' * 40)) {
    $ChangedFiles = @(& git diff --name-only $BaseCommit HEAD)
    if ($LASTEXITCODE -ne 0) { $ChangedFiles = @(); $global:LASTEXITCODE = 0 }
}
$docsOnly = @($ChangedFiles).Count -gt 0 -and @($ChangedFiles | Where-Object {
    $_ -cnotmatch '^(docs/|website/|README\.md$|CHANGELOG\.md$|contributors\.md$|contributing\.md$|zensical\.toml$)'
}).Count -eq 0
$infrastructure = @($ChangedFiles | Where-Object {
    $_ -cmatch '^(installer/|extras/|utils/|\.github/workflows/|global\.json$|NuGet\.config$|Directory\.)' -or
    $_ -cmatch '(\.csproj|packages\.lock\.json)$'
}).Count -gt 0 -or @($ChangedFiles | Where-Object { $_ }).Count -eq 0
if ($EventName -eq 'workflow_dispatch') {
    $package = $ManualPackage
    $reason = 'Explicit preview/rebuild request; run the full test gate.'
} elseif ($docsOnly) {
    $test = $false
    $reason = 'Documentation-only changes: text/site checks cover this source.'
} elseif ($EventName -eq 'push' -and $RefName -ceq $DefaultBranch) {
    $package = 'installer'
    $reason = 'Validate and compose the exact main release candidate once.'
} elseif ($EventName -eq 'pull_request') {
    if ($infrastructure) { $package = 'installer'; $reason = 'Packaging/infrastructure change requires pre-merge installer validation.' }
} elseif ($EventName -eq 'push') {
    try {
        if ($PullRequestLookup) { $requests = @(& $PullRequestLookup) }
        else {
            $owner = ($Repository -split '/',2)[0]
            $response = & gh api --method GET "repos/$Repository/pulls" -f "head=${owner}:$RefName" -f state=open -f per_page=100 --paginate --slurp
            if ($LASTEXITCODE -ne 0) { throw 'PR lookup failed' }
            $requests = @($response -join "`n" | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $_ })
        }
        $matching = @($requests | Where-Object {
            $_.state -eq 'open' -and $_.head.ref -ceq $RefName -and
            $_.head.repo.full_name -ceq $Repository -and $_.base.repo.full_name -ceq $Repository
        })
        if ($matching.Count) { $test = $false; $reason = 'The open PR validates the proposed merge; skip duplicate push tests.' }
    } catch {
        Write-Warning 'PR lookup unavailable; retain branch tests.'
        $global:LASTEXITCODE = 0
    }
}
$candidate = $test -and $package -eq 'installer' -and $RefName -ceq $DefaultBranch -and
    $Repository -ceq 'Pav-Osmolski/DS4Windows' -and $EventName -in @('push','workflow_dispatch')
$values = [ordered]@{test=$test.ToString().ToLowerInvariant();build=($package -ne 'none').ToString().ToLowerInvariant();package=$package;candidate=$candidate.ToString().ToLowerInvariant()}
if ($OutputPath) { foreach ($entry in $values.GetEnumerator()) { "$($entry.Key)=$($entry.Value)" | Add-Content -LiteralPath $OutputPath -Encoding utf8 } }
Write-Host "CI selection: $($values | ConvertTo-Json -Compress). $reason"
