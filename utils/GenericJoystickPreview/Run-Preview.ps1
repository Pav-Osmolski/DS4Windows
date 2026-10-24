param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$manifest = Get-Content "$root/manifest.json" -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.name))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) { throw "Package checksum failed: $($entry.name)" }
}
$backendEntries = @($manifest.files | Where-Object { $_.name -ceq 'viiper.exe' })
if ($backendEntries.Count -ne 1) { throw 'Missing matched backend checksum.' }
if ($ValidateOnly) { Write-Host 'Checksums validated. No applications or devices were started.'; exit 0 }
. "$root/Lab.Helpers.ps1"
if (-not [Environment]::Is64BitOperatingSystem) { throw 'This preview needs 64-bit Windows.' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Approve Windows elevation to attach and remove the temporary joystick.'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-NativeArgument "$root/Run-Preview.ps1")
    $child = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    exit $child.ExitCode
}
$mutex = New-Object Threading.Mutex($false, 'Global\DS4WindowsGenericJoystickLab')
$locked = $false; $backend = $null; $app = $null; $run = $null
$backendOut = $null; $backendErr = $null; $appOut = $null; $appErr = $null
$usbip = Join-Path ${env:ProgramFiles} 'USBip/usbip.exe'
$observed = @{}; $cleanupErrors = New-Object 'Collections.Generic.List[string]'; $failure = $null
function Read-Ports {
    $reply = Invoke-LabProcess $usbip @('port') $root
    if ($reply.Code -ne 0) { throw 'Cannot inspect USB/IP imports.' }
    @(Get-LabPorts $reply.Output)
}
function Test-BackendListeners {
    if ($null -eq $backend -or $backend.HasExited) { return $false }
    $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in @(3241, 3242) })
    foreach ($port in @(3241, 3242)) {
        $matches = @($listeners | Where-Object { $_.LocalPort -eq $port })
        if ($matches.Count -ne 1 -or $matches[0].OwningProcess -ne $backend.Id -or $matches[0].LocalAddress -ne '127.0.0.1') { return $false }
    }
    return $true
}
function Capture-OwnedPorts {
    if (-not (Test-BackendListeners)) { throw 'The preview backend lost its listening ports.' }
    foreach ($port in @(Read-Ports)) {
        if ($port.Host -ceq 'localhost' -and $port.ServerPort -eq 3241 -and $port.Device -match '^\d+-\d+$') {
            $observed[[string]$port.Port] = $port.Device
        }
    }
}
try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw 'Another DirectInput lab or preview is running.' }
    if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(DS4Windows|VIIPER|viiper-directinput-lab|GenericJoystickLabClient)$' }).Count -gt 0) {
        throw 'Exit installed DS4Windows and all VIIPER instances, then retry.'
    }
    if (-not (Test-Path -LiteralPath $usbip -PathType Leaf)) { throw 'Install the released USB/IP 0.9.8.1 runtime first.' }
    $version = Invoke-LabProcess $usbip @('--version') $root
    if ($version.Code -ne 0 -or $version.Output -notmatch '(?<!\d)0\.9\.8\.1(?!\d)') { throw 'USB/IP 0.9.8.1 is required.' }
    if (@(Read-Ports).Count -ne 0) { throw 'USB/IP imports are active. Close their owning applications first.' }
    $reservations = @()
    try {
        foreach ($port in @(3241, 3242)) {
            $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
            $listener.Start(); $reservations += $listener
        }
    } finally { foreach ($listener in $reservations) { $listener.Stop() } }
    $run = Join-Path $root ('results/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $null = New-Item -ItemType Directory -Path $run -Force
    $null = New-Item -ItemType Directory -Path "$root/lab-data" -Force
    $config = "$root/lab-data/backend-config.json"
    '{}' | Set-Content -LiteralPath $config -Encoding ASCII
    $backend = Start-LabProcess "$root/viiper.exe" @('--config-only', "--config=$config", '--log.level=debug', 'server',
        '--usb.addr=127.0.0.1:3241', '--api.addr=127.0.0.1:3242', '--api.require-local-host-auth=true',
        '--api.auto-attach-local-client=true', '--api.auto-attach-windows-native=true',
        '--api.device-handler-connect-timeout=120s', "--key-file=$root/lab-data/viiper.key.txt") $root
    $backendOut = $backend.StandardOutput.ReadToEndAsync(); $backendErr = $backend.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-BackendListeners) -or -not (Test-Path "$root/lab-data/viiper.key.txt")) {
        if ($backend.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw 'Backend startup failed; inspect the results logs.' }
        Start-Sleep -Milliseconds 200
    }
    Write-Host 'Create a fresh gamepad profile, then Advanced > Generic joystick (preview).'
    Write-Host 'When finished, EXIT DS4Windows from its tray menu to collect the report.'
    $app = Start-LabProcess "$root/DS4Windows.exe" @('--portable-lab', [string]$backendEntries[0].sha256) $root
    $appOut = $app.StandardOutput.ReadToEndAsync(); $appErr = $app.StandardError.ReadToEndAsync()
    while (-not $app.WaitForExit(1000)) { Capture-OwnedPorts }
    Capture-OwnedPorts
    if ($app.ExitCode -ne 0) { throw "Preview application exited with code $($app.ExitCode)." }
} catch { $failure = $_.Exception.Message } finally {
    if ($null -ne $app -and -not $app.HasExited) {
        $null = $app.CloseMainWindow()
        if (-not $app.WaitForExit(5000)) { $app.Kill(); $null = $app.WaitForExit(3000) }
    }
    if ($null -ne $backend) {
        try {
            if (Test-BackendListeners) {
                Capture-OwnedPorts
                foreach ($port in @(Read-Ports)) {
                    if ($port.Host -ceq 'localhost' -and $port.ServerPort -eq 3241 -and
                        $observed.ContainsKey([string]$port.Port) -and $observed[[string]$port.Port] -ceq $port.Device) {
                        $reply = Invoke-LabProcess $usbip @('detach', '-p', [string]$port.Port) $root
                        if ($reply.Code -ne 0) { $cleanupErrors.Add("Could not remove owned import $($port.Port).") }
                    }
                }
            } else { $cleanupErrors.Add('Backend ownership could not be verified; native cleanup was not attempted.') }
        } catch { $cleanupErrors.Add($_.Exception.Message) }
        if (-not $backend.HasExited) { $backend.Kill(); $null = $backend.WaitForExit(3000) }
    }
    if ($null -ne $run) {
        foreach ($capture in @(@('backend', $backendOut, $backendErr), @('application', $appOut, $appErr))) {
            if ($null -ne $capture[1] -and $capture[1].Wait(2000)) { $capture[1].Result | Set-Content "$run/$($capture[0]).log" -Encoding UTF8 }
            if ($null -ne $capture[2] -and $capture[2].Wait(2000)) { $capture[2].Result | Set-Content "$run/$($capture[0])-errors.log" -Encoding UTF8 }
        }
        $remaining = @()
        try { $remaining = @(Read-Ports) } catch { $cleanupErrors.Add($_.Exception.Message) }
        [ordered]@{ source = $manifest.source; failure = $failure; cleanupErrors = @($cleanupErrors); remainingImports = $remaining } |
            ConvertTo-Json -Depth 8 | Set-Content "$run/summary.json" -Encoding UTF8
        Get-ChildItem "$root/lab-data" -Filter '*.log' -File -Recurse | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $run ('ds4-' + $_.Name))
        }
        Compress-Archive -LiteralPath $run -DestinationPath "$run-report.zip"
        Write-Host "Report: $run-report.zip (check logs before sharing; no keys or profiles are included)."
        if ($remaining.Count -gt 0) { Write-Warning 'USB/IP imports remain. Check summary.json before restarting installed DS4Windows.' }
    }
    if ($null -ne $app) { $app.Dispose() }
    if ($null -ne $backend) { $backend.Dispose() }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
if ($null -ne $failure) { throw $failure }
if ($cleanupErrors.Count -gt 0) { throw ($cleanupErrors -join ' ') }
