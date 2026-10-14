$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Lab.Helpers.ps1"
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Block) {
    $failed = $false
    try { & $Block | Out-Null } catch { $failed = $true }
    Assert $failed 'Expected rejection.'
}
$text = @'
Imported USB devices
====================
Port 07: device in use at high-speed
         VIIPER Experimental Legacy Joystick
           -> usbip://localhost:49461/17-4
           -> remote bus/dev: 017/004
           -> serial:
           -> mode: low-latency
Port 08: device in use
           -> usbip://remote.example:49461/17-4
'@
$ports = @(Get-LabPorts $text)
Assert ($ports.Count -eq 2) 'Port parser count.'
$receipt = [pscustomobject]@{ port = 7; device = '4' }
Assert (Test-LabPortOwner $ports[0] 49461 17 $receipt) 'Exact owned import rejected.'
Assert (-not (Test-LabPortOwner $ports[1] 49461 17 $receipt)) 'Foreign import accepted.'
Assert (-not (Test-LabPortOwner $ports[0] 49462 17 $receipt)) 'Wrong server port accepted.'
Assert (-not (Test-LabPortOwner $ports[0] 49461 18 $receipt)) 'Wrong bus accepted.'
Assert (-not (Test-LabPortOwner $ports[0] 49461 17 ([pscustomobject]@{ port = 9; device = '4' }))) 'Wrong native port accepted.'
Assert (-not (Test-LabPortOwner $ports[0] 49461 17 ([pscustomobject]@{ port = 7; device = '5' }))) 'Wrong device accepted.'
Assert (Test-LabPortOwner $ports[0] 49461 17 $null) 'Allocated bus fallback rejected.'
Assert (@(Get-LabPorts '').Count -eq 0) 'Empty imports.'
Assert-Throws { Get-LabPorts "Port 1: device in use`n -> usbip://localhost:49461/1-1`n -> usbip://localhost:49461/1-2" }
Assert-Throws { Get-LabPorts 'Port 1: device in use' }
Assert-Throws { Get-LabPorts "Port 1: device in use`n -> usbip://localhost:49461/1-1`nPort 1: device in use`n -> usbip://localhost:49461/1-1" }
Assert-Throws { Get-LabPorts "Port 1: device in use`n -> usbip://user@localhost:49461/1-1" }
$reading = [pscustomobject]@{ axes = @(0, 0, 32767, -32767, 0, 0); buttons = 1; hat = 8; axisCount = 6; buttonCount = 12; hatCount = 1 }
$expected = [pscustomobject]@{ axes = $reading.axes; buttons = 1; hat = 8 }
$probe = [pscustomobject]@{ error = $false; directInput = @($reading); winMM = @($reading) }
Assert (@(Get-LabReadingErrors $probe $expected).Count -eq 0) 'Matching readings rejected.'
$expected.buttons = 0
Assert (@(Get-LabReadingErrors $probe $expected).Count -eq 2) 'Wrong button mask was missed.'
$expected.buttons = 1; $expected.axes = @(0, 0, 0, 0, 0, 0)
Assert (@(Get-LabReadingErrors $probe $expected).Count -eq 4) 'Wrong axis roles were missed.'
$probe.directInput = @()
Assert ((@(Get-LabReadingErrors $probe $expected) -join ' ') -like '*found 0 devices*') 'Missing enumeration was missed.'
$probe.directInput = @($reading, $reading)
Assert ((@(Get-LabReadingErrors $probe $expected) -join ' ') -like '*found 2 devices*') 'Ambiguous enumeration was missed.'
# Exercise process transport and native argument escaping with spaces, quotes,
# trailing backslashes and Unicode using Windows PowerShell 5.1 itself.
$script = 'param([string]$Value) [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); [Console]::Write($Value)'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('lab-quote-' + [Guid]::NewGuid().ToString('N') + '.ps1')
try {
    $script | Set-Content -LiteralPath $temp -Encoding UTF8
    $value = 'space "quote" Unicode ' + [char]0x0142 + '\'
    $result = Invoke-LabProcess "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" @('-NoProfile', '-File', $temp, $value) $PSScriptRoot
    Assert ($result.Code -eq 0 -and $result.Output -ceq $value) 'Native argument round trip failed.'
    Assert-Throws { Invoke-LabProcess "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" @('-NoProfile', '-Command', 'Start-Sleep -Seconds 5') $PSScriptRoot 100 }
} finally { Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue }
Write-Host 'Lab ownership, API comparison, argument quoting and timeout tests passed. No driver operations performed.'
