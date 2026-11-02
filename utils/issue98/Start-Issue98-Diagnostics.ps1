$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'DS4Windows.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Extract the complete preview ZIP first.' }
if (Get-Process -Name DS4Windows -ErrorAction SilentlyContinue) {
    throw 'Fully exit your normal DS4Windows (including its tray icon), then run this launcher again. Nothing was stopped.'
}
$diag = Join-Path $root 'Diagnostics'
New-Item -ItemType Directory -Path $diag -Force | Out-Null
$report = Join-Path $diag 'issue98-adapter-info.txt'
"Issue #98 read-only adapter inventory`r`nCaptured: $([DateTime]::Now.ToString('o'))" | Set-Content -LiteralPath $report -Encoding UTF8
try {
    Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPClass -eq 'Bluetooth' -or $_.Service -eq 'BTHUSB' } |
        Select-Object Name,DeviceID,Service,Status,ConfigManagerErrorCode |
        Format-List | Out-String | Add-Content -LiteralPath $report -Encoding UTF8
    Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceClass -eq 'Bluetooth' } |
        Select-Object DeviceName,DeviceID,DriverVersion,DriverProviderName,DriverDate |
        Format-List | Out-String | Add-Content -LiteralPath $report -Encoding UTF8
    foreach ($radio in (Get-PnpDevice -Class Bluetooth -PresentOnly | Where-Object { $_.InstanceId -like 'USB\*' })) {
        $node = $radio.InstanceId
        for ($depth = 0; $depth -lt 6 -and $node; $depth++) {
            "USB parent depth ${depth}: $node" | Add-Content -LiteralPath $report -Encoding UTF8
            $node = (Get-PnpDeviceProperty -InstanceId $node -KeyName 'DEVPKEY_Device_Parent' -ErrorAction Stop).Data
        }
    }
} catch { "Inventory limitation: $($_.Exception.Message)" | Add-Content -LiteralPath $report -Encoding UTF8 }
try {
    Get-CimInstance -Namespace root/wmi -ClassName MSPower_DeviceEnable -ErrorAction Stop |
        Where-Object { $_.InstanceName -match 'BTH|USB' } | Select-Object InstanceName,Enable |
        Format-List | Out-String | Add-Content -LiteralPath $report -Encoding UTF8
} catch { "Power inventory unavailable: $($_.Exception.Message)" | Add-Content -LiteralPath $report -Encoding UTF8 }
& powercfg.exe /getactivescheme 2>&1 | Add-Content -LiteralPath $report -Encoding UTF8
Write-Host 'Testing only: Issue #98 diagnostic preview.'
Write-Host 'No drivers, power settings or Windows sound defaults are changed.'
Write-Host 'The diagnostic window opens alongside DS4Windows. ZIPs are saved in the Diagnostics folder.'
$previous = $env:DS4WINDOWS_ISSUE98_DIAGNOSTICS
try {
    $env:DS4WINDOWS_ISSUE98_DIAGNOSTICS = '1'
    Start-Process -FilePath $exe -WorkingDirectory $root -PassThru | Out-Null
} finally { $env:DS4WINDOWS_ISSUE98_DIAGNOSTICS = $previous }
