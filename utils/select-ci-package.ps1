[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EventName,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$RefName,
    [Parameter(Mandatory)][string]$DefaultBranch,
    [string]$OutputPath = $env:GITHUB_OUTPUT,
    [scriptblock]$PullRequestLookup
)

$ErrorActionPreference = 'Stop'
$build = $true
$reason = 'PR, manual or default-branch packages are always built.'
if ($EventName -eq 'push' -and $RefName -cne $DefaultBranch) {
    try {
        if ($PullRequestLookup) {
            $requests = @(& $PullRequestLookup)
        }
        else {
            $owner = ($Repository -split '/', 2)[0]
            $response = & gh api --method GET "repos/$Repository/pulls" `
                -f "head=${owner}:$RefName" -f state=open -f per_page=100 `
                --paginate --slurp
            if ($LASTEXITCODE -ne 0) { throw 'Pull-request lookup failed.' }
            $pages = ($response -join "`n") | ConvertFrom-Json
            $requests = @($pages | ForEach-Object { $_ } | ForEach-Object { $_ })
        }
        $matching = @($requests | Where-Object {
            $_.state -eq 'open' -and $_.head.ref -ceq $RefName -and
            $_.head.repo.full_name -ceq $Repository -and
            $_.base.repo.full_name -ceq $Repository
        })
        if ($matching.Count -gt 0) {
            $build = $false
            $reason = 'Open PR builds the proposed merge; branch tests still run.'
        }
        else {
            $reason = 'No matching open PR; preserve the branch package.'
        }
    }
    catch {
        # Missing API access must cost an extra build, never omit validation.
        Write-Warning 'Could not check for an open PR; building the branch package.'
        $reason = 'Lookup unavailable; preserve the branch package.'
        $global:LASTEXITCODE = 0
    }
}
$value = $build.ToString().ToLowerInvariant()
if ($OutputPath) { "build=$value" | Add-Content -LiteralPath $OutputPath -Encoding utf8 }
Write-Host "Package selection: $value. $reason"
