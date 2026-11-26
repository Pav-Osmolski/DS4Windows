param([Parameter(Mandatory)][string]$ProbeDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
foreach($architecture in @('x64','x86')) {
    $probe=Join-Path $ProbeDirectory "GenericJoystickProbe-$architecture.exe"
    & $probe --self-test
    if($LASTEXITCODE -ne 0){throw "$architecture normalization self-test failed."}
    $fixture=& $probe --self-test-json
    if($LASTEXITCODE -ne 0){throw "$architecture identity JSON self-test failed."}
    $snapshot=$fixture|ConvertFrom-Json
    $expectedBits=if($architecture -eq 'x64'){64}else{32}
    if($snapshot.architecture -ne $expectedBits -or $snapshot.directInput.Count -ne 2 -or $snapshot.error){throw 'Invalid no-device fixture.'}
    $valid=$snapshot.directInput[0]
    if($valid.instance -cne 'enumerated' -or $valid.openedIdentity.instance -cne 'opened' -or $valid.openedIdentity.deviceInfoResult -ne 0 -or $valid.openedIdentity.joystickIdResult -ne 0 -or $valid.openedIdentity.joystickId -ne 0){throw 'Enumerated identity, opened identity or valid slot zero was lost.'}
    $unavailable=$snapshot.directInput[1].openedIdentity
    if($unavailable.deviceInfoResult -ge 0 -or $unavailable.joystickIdResult -ge 0 -or $null -ne $unavailable.instance -or $null -ne $unavailable.joystickId){throw 'Unavailable query must retain HRESULT and null value.'}
}
Write-Host 'PASS: x86/x64 normalization and identity JSON; failed queries remain distinct from valid slot zero. No device access.'
