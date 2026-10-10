param([string]$WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/release.yml'))
$ErrorActionPreference = 'Stop'

# Execute the real identity gate with a recording GitHub API substitute.
# No release, tag, certificate or network resource is created by this test.
$workflow = Get-Content -LiteralPath $WorkflowPath -Raw
$identity = [regex]::Match($workflow, '(?ms)^  identity:\r?\n(?<identity>.*?)^  release:\r?$').Groups['identity'].Value
$match = [regex]::Match($identity, '(?ms)^      run: \|\r?\n(?<script>.*)\z')
if (-not $match.Success) { throw 'Missing release identity script.' }
$code = ($match.Groups['script'].Value -split '\r?\n' | ForEach-Object {
    if ($_.StartsWith('        ')) { $_.Substring(8) } else { $_ }
}) -join "`n"
$gate = [scriptblock]::Create($code)
$sha = 'a' * 40
$cases = @(
    @{ Tag='5.0.14'; Accept=$true; Unsigned=$true; Verify=$false },
    @{ Tag='5.0.15'; Accept=$true; Unsigned=$true; Verify=$false },
    @{ Tag='6.0.0'; Accept=$true; Unsigned=$true; Verify=$false },
    @{ Tag='5.0.14'; Published=$true; Accept=$true; Unsigned=$true; Verify=$true },
    @{ Tag='VIIPERRC4.6.8'; Published=$true; Pre=$true; Accept=$true; Unsigned=$true; Verify=$true },
    @{ Tag='5.0.14'; Pre=$true; Accept=$false },
    @{ Tag='5.0.13'; Accept=$false },
    @{ Tag='5.0.14.0'; Accept=$false },
    @{ Tag='v5.0.14'; Accept=$false },
    @{ Tag='05.0.14'; Accept=$false },
    @{ Tag='5.0.14-rc1'; Accept=$false },
    @{ Tag='VIIPERRC4.6.8'; Pre=$true; Accept=$false },
    @{ Tag='5.0.14'; Repo='other/DS4Windows'; Accept=$false },
    @{ Tag='5.0.14'; Published=$true; Repo='other/DS4Windows'; Accept=$true; Unsigned=$false; Verify=$false },
    @{ Tag='5.0.14'; WrongTarget=$true; Accept=$false },
    @{ Tag='5.0.14'; ExistingTag=$true; WrongTarget=$true; Accept=$false },
    @{ Tag='5.0.14'; ExistingTag=$true; Accept=$true; Unsigned=$true; Verify=$false }
)
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "ds4w-release-policy-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempRoot | Out-Null
function gh {
    $call = $args -join ' '
    if ($call -match 'api --paginate --slurp') { return ConvertTo-Json -InputObject @(@($script:mockRelease)) -Depth 5 -Compress }
    if ($call -match 'git/matching-refs') {
        $refs = if ($script:case.ExistingTag) { @(@{ ref="refs/tags/$($script:case.Tag)" }) } else { @() }
        return ConvertTo-Json -InputObject $refs -Compress
    }
    if ($call -match 'api --method POST') { $script:created++; return '{}' }
    if ($call -match '/commits/') {
        return @{sha= $(if ($script:case.WrongTarget) { 'b' * 40 } else { $sha })} | ConvertTo-Json -Compress
    }
    if ($call -match '/releases/') { return $script:mockRelease | ConvertTo-Json -Compress }
    throw "Unexpected API request: $call"
}
foreach ($script:case in $cases) {
    $script:created = 0
    $script:mockRelease = @{id=123;tag_name=$case.Tag;draft= -not $case.Published;prerelease=[bool]$case.Pre;target_commitish='main'}
    $env:GITHUB_REPOSITORY = if ($case.Repo) { $case.Repo } else { 'Pav-Osmolski/DS4Windows' }
    $env:GITHUB_SHA = $sha
    $env:GITHUB_EVENT_NAME = if ($case.Published) { 'release' } else { 'workflow_dispatch' }
    $env:INPUT_TAG = $case.Tag
    $env:EVENT_TAG = $case.Tag
    $env:EVENT_RELEASE_ID = '123'
    $env:EVENT_PRERELEASE = ([bool]$case.Pre).ToString().ToLowerInvariant()
    $env:GITHUB_OUTPUT = Join-Path $tempRoot "$($cases.IndexOf($case)).outputs"
    $env:GITHUB_STEP_SUMMARY = Join-Path $tempRoot 'summary'
    $accepted = $false
    try { & $gate | Out-Null; $accepted = $true } catch {
        if ($case.Accept) { throw }
    }
    if ($accepted -ne $case.Accept) { throw "Unexpected acceptance for $($case | ConvertTo-Json -Compress)" }
    if ($accepted) {
        $outputs = Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw
        foreach ($entry in @("unsigned_release=$($case.Unsigned.ToString().ToLowerInvariant())", "verify_existing=$($case.Verify.ToString().ToLowerInvariant())")) {
            if (-not $outputs.Contains($entry)) { throw "Missing expected output: $entry" }
        }
    } elseif ($created -ne 0 -or (Test-Path -LiteralPath $env:GITHUB_OUTPUT)) {
        throw 'Rejected release created a tag or exposed job outputs.'
    }
}
Write-Host "Release identity checks passed: $($cases.Count) cases. No network or release mutations."
