$ErrorActionPreference='Stop'
$repo='Pav-Osmolski/DS4Windows'
$request=[pscustomobject]@{state='open';head=@{ref='feature/example';repo=@{full_name=$repo}};base=@{repo=@{full_name=$repo}}}
$cases=@(
 @{Event='push';Branch='feature/example';Requests=@($request);Test='false';Package='none'},
 @{Event='push';Branch='feature/example';Requests=@();Test='true';Package='none'},
 @{Event='push';Branch='feature/other';Requests=@($request);Test='true';Package='none'},
 @{Event='push';Branch='feature/example';Fails=$true;Test='true';Package='none'},
 @{Event='push';Branch='main';Test='true';Package='installer';Candidate='true'},
 @{Event='push';Branch='main';Files=@('docs/troubleshooting.md','CHANGELOG.md');Test='false';Package='none'},
 @{Event='pull_request';Branch='123/merge';Files=@('DS4Windows/View.xaml');Test='true';Package='none'},
 @{Event='pull_request';Branch='123/merge';Files=@('installer/build-installer.ps1');Test='true';Package='installer'},
 @{Event='pull_request';Branch='123/merge';Files=@('.github/workflows/ci-build.yml');Test='true';Package='installer'},
 @{Event='pull_request';Branch='123/merge';Files=@('docs/a.md','DS4Windows/View.xaml');Test='true';Package='none'},
 @{Event='pull_request';Branch='123/merge';Files=@('README.md');Test='false';Package='none'},
 @{Event='pull_request';Branch='123/merge';Test='true';Package='installer'},
 @{Event='push';Branch='feature/example';Requests=@([pscustomobject]@{state='closed';head=$request.head;base=$request.base});Test='true';Package='none'},
 @{Event='push';Branch='feature/example';Requests=@([pscustomobject]@{state='open';head=$request.head;base=@{repo=@{full_name='other/repo'}}});Test='true';Package='none'},
 @{Event='workflow_dispatch';Branch='feature/example';Test='true';Package='portable'},
 @{Event='workflow_dispatch';Branch='feature/example';Manual='installer';Test='true';Package='installer'},
 @{Event='workflow_dispatch';Branch='main';Manual='installer';Test='true';Package='installer';Candidate='true'},
 @{Event='workflow_dispatch';Branch='main';Manual='none';Test='true';Package='none'},
 @{Event='push';Branch='feature/example';Requests=@([pscustomobject]@{state='open';head=@{ref='feature/example';repo=@{full_name='other/repo'}};base=$request.base});Test='true';Package='none'}
)
$output=Join-Path ([IO.Path]::GetTempPath()) ('ds4w-ci-selection-'+[guid]::NewGuid()+'.txt')
try {
 foreach($case in $cases) {
  [IO.File]::WriteAllText($output,'')
  $arguments=@{EventName=$case.Event;Repository=$repo;RefName=$case.Branch;DefaultBranch='main';ChangedFiles=@($case.Files);OutputPath=$output;PullRequestLookup={if($case.Fails){throw 'API failure'};$case.Requests}}
  if($case.Manual){$arguments.ManualPackage=$case.Manual}
  & (Join-Path $PSScriptRoot 'ci-plan.ps1') @arguments
  $actual=ConvertFrom-StringData (Get-Content -LiteralPath $output -Raw)
  $candidate=if($case.Candidate){$case.Candidate}else{'false'}
  if($actual.test -cne $case.Test -or $actual.package -cne $case.Package -or $actual.candidate -cne $candidate -or
     $actual.build -cne ($case.Package -ne 'none').ToString().ToLowerInvariant()){throw "Wrong selection: $($case|ConvertTo-Json -Compress)"}
 }
 Write-Host "CI selection passed: $($cases.Count) cases; no network."
} finally {Remove-Item -LiteralPath $output -ErrorAction SilentlyContinue}
