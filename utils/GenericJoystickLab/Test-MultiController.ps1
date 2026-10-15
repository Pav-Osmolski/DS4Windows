$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Lab.Helpers.ps1"
. "$PSScriptRoot/MultiController.Helpers.ps1"
function Reading($Id,$Axes) { [pscustomobject]@{instance=$Id; axes=$Axes; buttons=0; hat=8; axisCount=6; buttonCount=12; hatCount=1} }
$a = Reading 'A' @(100,0,0,0,0,0); $b = Reading 'B' @(-100,0,0,0,0,0)
$expected = @([pscustomobject]@{name='A';state=$a},[pscustomobject]@{name='B';state=$b})
$probe = [pscustomobject]@{error=$false;directInput=@($b,$a);winMM=@($a,$b)}
$baseline = @{A='A';B='B'}
function Check([bool]$Pass,[string]$Label) { if (-not $Pass) { throw $Label } }
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -eq 0) 'Reordered input should pass.'
$probe.winMM = @((Reading '7' $a.axes),(Reading '3' $b.axes))
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -eq 0) 'WinMM slots may reorder.'
$probe.directInput = @((Reading 'CHANGED' $a.axes),$b)
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -gt 0) 'Changed DirectInput identity must fail.'
$probe.directInput = @($a,(Reading 'A' $b.axes))
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -gt 0) 'Duplicate identity must fail.'
$probe.directInput = @($a)
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -gt 0) 'Missing controller must fail.'
$probe.directInput = @($a,(Reading 'B' $a.axes))
Check (@(Get-MultiControllerReadingErrors $probe $expected $baseline).Count -gt 0) 'Crossed input must fail.'
$probe.directInput = @((Reading 'A' @(0,0,0,0,0,0)),(Reading 'B' @(0,0,0,0,0,0)))
$probe.winMM = $probe.directInput
$neutral = $probe.directInput[0]
$pair = @([pscustomobject]@{name='A';state=$neutral},[pscustomobject]@{name='B';state=$neutral})
Check (@(Get-MultiControllerReadingErrors $probe $pair @{} -EnumerationOnly).Count -eq 0) 'Neutral enumeration should pass.'
$tokens=$null; $errors=$null
$null = [Management.Automation.Language.Parser]::ParseFile("$PSScriptRoot/Run-MultiControllerLab.ps1",[ref]$tokens,[ref]$errors)
Check ($errors.Count -eq 0) 'Runner syntax error.'
Write-Host 'PASS: pair matching, identity, slot reordering, missing/crossed input, neutral enumeration and runner syntax.'
# Exercise the actual runner's per-device lifecycle with fake process boundaries.
# No executables, elevation or driver operations are invoked here.
$ast = [Management.Automation.Language.Parser]::ParseFile("$PSScriptRoot/Run-MultiControllerLab.ps1",[ref]$tokens,[ref]$errors)
foreach ($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Start-Device','Stop-Device')},$false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}
$root = $PSScriptRoot; $run = Join-Path ([IO.Path]::GetTempPath()) ('joystick-lab-test-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory $run
$keyPath='unused'; $apiPort=12345; $devices=@{}; $cleanupErrors=New-Object 'Collections.Generic.List[string]'
$plan=[pscustomobject]@{options=[pscustomobject]@{version=1;experimental=$true;buttonCount=12;neutralAxes=@(0,0,0,0,0,0)}}
$allocatedBus=0; $failReceipt=$false; $removed=New-Object 'Collections.Generic.List[int]'; $stopped=New-Object 'Collections.Generic.List[int]'
function Start-LabProcess {
    $script:allocatedBus++
    $reader=[pscustomobject]@{}
    $reader | Add-Member ScriptMethod ReadToEndAsync { return 'fake stderr task' }
    [pscustomobject]@{bus=$script:allocatedBus;StandardError=$reader;reply=0}
}
function Read-LabReply($Process) {
    $Process.reply++
    if ($Process.reply -eq 1) { return [pscustomobject]@{kind='allocated';bus=$Process.bus} }
    if ($script:failReceipt) { throw 'Simulated failure after allocation.' }
    [pscustomobject]@{kind='ready';bus=$Process.bus;device='1';port=$Process.bus;vid='0xffff';pid='0xd10c'}
}
function Remove-OwnedImport { $removed.Add([int]$script:bus) }
function Stop-LabClient { $stopped.Add([int]$script:client.bus) }
try {
    Start-Device A; Start-Device B
    Check ($devices.Count -eq 2) 'Both clients should be tracked.'
    $aId=(Get-Content "$run/options-A.json" -Raw|ConvertFrom-Json).instanceId
    $bId=(Get-Content "$run/options-B.json" -Raw|ConvertFrom-Json).instanceId
    Check ($aId -ne $bId) 'Synthetic identities must differ.'
    Stop-Device A
    Check ($devices.ContainsKey('B') -and $removed.Count -eq 1 -and $removed[0] -eq 1) 'Removing A must preserve B.'
    Start-Device A
    Check ((Get-Content "$run/options-A.json" -Raw|ConvertFrom-Json).instanceId -ceq $aId) 'Recreation must reuse A identity.'
    Stop-Device A; Stop-Device B
    $failReceipt=$true
    try { Start-Device A } catch { }
    Check ($devices.A.bus -eq 4) 'Allocated bus must survive a failed receipt.'
    Stop-Device A
    Check ($removed.Count -eq 4 -and $stopped.Count -eq 4 -and $devices.Count -eq 0) 'All allocated clients must be cleaned independently.'
} finally {
    # Only explicit test files in the fresh temporary directory are removed.
    Remove-Item -LiteralPath "$run/options-A.json","$run/options-B.json"
    Remove-Item -LiteralPath $run
}
Write-Host 'PASS: two-client tracking, distinct/stable IDs and independent cleanup after recreation and failed attachment receipt.'
