$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'select-ci-package.ps1'
$repo = 'example/DS4Windows'
$request = [pscustomobject]@{
    state = 'open'
    head = @{ ref = 'feature/example'; repo = @{ full_name = $repo } }
    base = @{ repo = @{ full_name = $repo } }
}
$cases = @(
    @{ Event='push'; Branch='feature/example'; Requests=@($request); Expected='false' },
    @{ Event='push'; Branch='feature/example'; Requests=@(); Expected='true' },
    @{ Event='pull_request'; Branch='123/merge'; Requests=@($request); Expected='true' },
    @{ Event='workflow_dispatch'; Branch='feature/example'; Requests=@($request); Expected='true' },
    @{ Event='push'; Branch='main'; Requests=@($request); Expected='true' },
    @{ Event='push'; Branch='feature/example'; Fails=$true; Expected='true' },
    @{ Event='push'; Branch='feature/other'; Requests=@($request); Expected='true' },
    @{ Event='push'; Branch='feature/example'; Requests=@([pscustomobject]@{
        state='closed'; head=$request.head; base=$request.base }); Expected='true' },
    @{ Event='push'; Branch='feature/example'; Requests=@([pscustomobject]@{
        state='open'; head=@{ref='feature/example';repo=@{full_name='outsider/DS4Windows'}};
        base=$request.base }); Expected='true' },
    @{ Event='push'; Branch='feature/example'; Requests=@([pscustomobject]@{
        state='open'; head=$request.head; base=@{repo=@{full_name='other/DS4Windows'}} }); Expected='true' }
)
$output = Join-Path ([IO.Path]::GetTempPath()) ('ds4w-ci-selection-' + [guid]::NewGuid() + '.txt')
try {
    foreach ($case in $cases) {
        [IO.File]::WriteAllText($output, '')
        $lookup = { if ($case.Fails) { throw 'Simulated API failure' }; $case.Requests }
        & $script -EventName $case.Event -Repository $repo -RefName $case.Branch `
            -DefaultBranch main -OutputPath $output -PullRequestLookup $lookup
        $actual = (Get-Content -LiteralPath $output -Raw).Trim()
        if ($actual -cne "build=$($case.Expected)") { throw "Wrong selection for $($case.Event)/$($case.Branch): $actual" }
    }
    Write-Host "CI package selection passed: $($cases.Count) cases. No network calls."
}
finally { Remove-Item -LiteralPath $output -ErrorAction SilentlyContinue }
