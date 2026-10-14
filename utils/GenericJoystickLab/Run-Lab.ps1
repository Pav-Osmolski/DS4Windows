param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Lab.Helpers.ps1"

$root = [IO.Path]::GetFullPath($PSScriptRoot)
# Validate payloads before running any executable, including before elevation.
$manifest = Get-Content "$root/manifest.json" -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.name))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) { throw "Package checksum failed: $($entry.name)" }
}
$plans = Get-Content "$root/plans.json" -Raw | ConvertFrom-Json
if (@($plans).Count -ne 8) { throw 'Expected eight mapping plans.' }
if ($ValidateOnly) { Write-Host 'Package checksums and mapping plans validated. No devices or processes were started.'; exit 0 }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'This lab package needs 64-bit Windows.' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Windows administrator approval is needed to attach and remove the temporary joystick.'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-NativeArgument "$root/Run-Lab.ps1")
    $child = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    exit $child.ExitCode
}

$mutex = New-Object Threading.Mutex($false, 'Global\DS4WindowsGenericJoystickLab')
$locked = $false
$backend = $null; $backendOut = $null; $backendErr = $null
$client = $null; $clientErr = $null; $receipt = $null; $bus = $null
$rows = New-Object 'Collections.Generic.List[object]'
$cleanupErrors = New-Object 'Collections.Generic.List[string]'
$failure = $null; $run = $null; $usbip = $null; $usbPort = 0

function Read-Ports {
    $result = Invoke-LabProcess $usbip @('port') $root
    if ($result.Code -ne 0) { throw "Cannot inspect USB/IP imports: $($result.Errors)" }
    @(Get-LabPorts $result.Output)
}
function Remove-OwnedImport {
    if ($null -eq $bus) { return }
    $ports = @(Read-Ports)
    $owned = @($ports | Where-Object { Test-LabPortOwner $_ $usbPort $bus $receipt })
    if ($owned.Count -gt 1) { throw 'More than one import matches the lab device; cleanup refused.' }
    foreach ($port in $owned) {
        $result = Invoke-LabProcess $usbip @('detach', '-p', [string]$port.Port) $root
        if ($result.Code -ne 0) { throw "Could not detach owned port $($port.Port): $($result.Errors)" }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $remaining = @(Read-Ports | Where-Object { Test-LabPortOwner $_ $usbPort $bus $receipt })
        if ($remaining.Count -eq 0) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The lab import did not disappear after detach.'
}
function Stop-LabClient {
    if ($null -eq $client) { return }
    try {
        if (-not $client.HasExited) {
            Send-LabCommand $client 'quit'
            if (-not $client.WaitForExit(5000)) { throw 'Lab client did not exit after cleanup.' }
        }
    } finally {
        if (-not $client.HasExited) { $client.Kill(); $null = $client.WaitForExit(3000) }
        if ($null -ne $clientErr -and $clientErr.Wait(2000)) { Add-Content -LiteralPath "$run/client-errors.log" -Value $clientErr.Result }
        $client.Dispose()
    }
}
function Capture-Step([string]$Plan, [string]$Label, $Expected, [int]$RetrySeconds = 0) {
    foreach ($architecture in @('x64', 'x86')) {
        $deadline = [DateTime]::UtcNow.AddSeconds($RetrySeconds)
        do {
            $result = Invoke-LabProcess "$root/GenericJoystickProbe-$architecture.exe" @('--json') $root
            $probe = $null; $problems = @()
            try {
                $probe = $result.Output | ConvertFrom-Json
                $problems = @(Get-LabReadingErrors $probe $Expected)
                if ($result.Code -ne 0) { $problems += "Probe exit code $($result.Code): $($result.Errors)" }
            } catch { $problems = @("Could not read probe JSON: $($result.Output) $($result.Errors)") }
            if ($problems.Count -eq 0 -or [DateTime]::UtcNow -ge $deadline) { break }
            Start-Sleep -Milliseconds 250
        } while ($true)
        $rows.Add([pscustomobject]@{ plan = $Plan; step = $Label; architecture = $architecture; expected = $Expected; observed = $probe; errors = $problems })
        # Preserve all observations, even if later cleanup encounters an error.
        $rows | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$run/readings.json" -Encoding UTF8
        if ($problems.Count -gt 0) { throw "$Plan / $Label / $architecture : $($problems -join ' ')" }
    }
}

try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw 'Another joystick lab is already running.' }
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(DS4Windows|VIIPER|viiper-directinput-lab|GenericJoystickLabClient)$' })
    if ($running.Count -gt 0) { throw 'Close DS4Windows and all VIIPER instances, then run START.cmd again.' }
    $usbip = Join-Path ${env:ProgramFiles} 'USBip/usbip.exe'
    if (-not (Test-Path -LiteralPath $usbip -PathType Leaf)) { throw 'Install the released USB/IP 0.9.8.1 runtime using DS4Windows setup first.' }
    $version = Invoke-LabProcess $usbip @('--version') $root
    if ($version.Code -ne 0 -or $version.Output -notmatch '(?<!\d)0\.9\.8\.1(?!\d)') { throw 'The lab requires the installed USB/IP 0.9.8.1 runtime.' }
    if (@(Read-Ports).Count -ne 0) { throw 'USB/IP imports are already active. Close their owning applications and retry; the lab will not remove them.' }
    # Reserve two distinct ephemeral loopback ports until just before launch.
    $usbListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $apiListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    try {
        $usbListener.Start(); $apiListener.Start()
        $usbPort = $usbListener.LocalEndpoint.Port; $apiPort = $apiListener.LocalEndpoint.Port
    } finally { $usbListener.Stop(); $apiListener.Stop() }
    $run = Join-Path $root ('results/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $null = New-Item -ItemType Directory -Path $run -Force
    $keyPath = Join-Path $run 'lab.key.txt'
    $configPath = Join-Path $run 'lab-config.json'
    '{}' | Set-Content -LiteralPath $configPath -Encoding ASCII
    Write-Host 'Testing eight layouts/trigger modes through 32-bit and 64-bit Windows APIs.'
    Write-Host 'This uses synthetic inputs; you do not need to move your controller.'
    $backend = Start-LabProcess "$root/viiper-directinput-lab.exe" @(
        '--config-only', "--config=$configPath", '--log.level=info', 'server',
        "--usb.addr=127.0.0.1:$usbPort", "--api.addr=127.0.0.1:$apiPort",
        '--api.require-local-host-auth=true', '--api.auto-attach-local-client=true',
        '--api.auto-attach-windows-native=true', '--api.device-handler-connect-timeout=120s', "--key-file=$keyPath"
    ) $root
    $backendOut = $backend.StandardOutput.ReadToEndAsync(); $backendErr = $backend.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        if ($backend.HasExited) { throw 'Lab backend exited during startup; see its report logs.' }
        if (Test-Path -LiteralPath $keyPath) {
            $socket = New-Object Net.Sockets.TcpClient
            try { $socket.Connect('127.0.0.1', $apiPort); break } catch {} finally { $socket.Dispose() }
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Lab backend startup timed out.' }
        Start-Sleep -Milliseconds 200
    } while ($true)
    foreach ($plan in $plans) {
        Write-Host "  $($plan.name)"
        $bus = $null; $receipt = $null
        $optionsPath = Join-Path $run 'device-options.json'
        $plan.options | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $optionsPath -Encoding ASCII
        $client = Start-LabProcess "$root/GenericJoystickLabClient.exe" @("--api=127.0.0.1:$apiPort", "--key-file=$keyPath", "--options-file=$optionsPath") $root
        $clientErr = $client.StandardError.ReadToEndAsync()
        try {
            $allocated = Read-LabReply $client
            if ($allocated.kind -ne 'allocated' -or [uint32]$allocated.bus -eq 0) { throw 'Invalid lab bus allocation.' }
            $bus = [uint32]$allocated.bus
            $candidate = Read-LabReply $client
            if ($candidate.kind -ne 'ready' -or $candidate.bus -ne $bus -or $candidate.port -le 0 -or $candidate.vid -ne '0xffff' -or $candidate.pid -ne '0xd10c' -or [string]$candidate.device -notmatch '^\d+$') { throw 'Invalid native attachment receipt.' }
            $receipt = $candidate
            # Initial Windows SET_CONFIGURATION can retire a producer opened
            # immediately after IOCTL attach. Prove both consumers see neutral
            # before creating the first producer; never silently retry resets.
            Capture-Step $plan.name 'Windows enumeration before producer' $plan.neutral 15
            Send-LabCommand $client 'reconnect'
            foreach ($step in $plan.steps) {
                Send-LabCommand $client $step.wire
                Start-Sleep -Milliseconds 350
                Capture-Step $plan.name $step.label $step 5
            }
            # Lose a producer while controls are held, then verify configured
            # neutral and resynchronization without replacing the HID device.
            $held = @($plan.steps | Where-Object { $_.label -eq 'Left trigger full' })[0]
            Send-LabCommand $client $held.wire
            Start-Sleep -Milliseconds 200
            Send-LabCommand $client 'disconnect'
            Start-Sleep -Milliseconds 350
            Capture-Step $plan.name 'Producer disconnected: neutral' $plan.neutral 3
            Send-LabCommand $client 'reconnect'
            Send-LabCommand $client $held.wire
            Start-Sleep -Milliseconds 350
            Capture-Step $plan.name 'Producer reconnected: held trigger' $held 3
        } finally {
            try { Remove-OwnedImport } catch { $cleanupErrors.Add($_.Exception.Message) }
            try { Stop-LabClient } catch { $cleanupErrors.Add($_.Exception.Message) }
            $client = $null; $clientErr = $null
        }
        if ($cleanupErrors.Count -gt 0) { throw 'Lab cleanup failed; stopping further tests.' }
        if (@(Read-Ports).Count -ne 0) { throw 'An unexpected USB/IP import appeared; stopping further tests.' }
    }
} catch {
    $failure = $_.Exception.Message
    Write-Host "Test stopped: $failure" -ForegroundColor Red
} finally {
    if ($null -ne $client) {
        try { Remove-OwnedImport } catch { $cleanupErrors.Add($_.Exception.Message) }
        try { Stop-LabClient } catch { $cleanupErrors.Add($_.Exception.Message) }
    }
    if ($null -ne $backend) {
        if (-not $backend.HasExited) { $backend.Kill(); $null = $backend.WaitForExit(5000) }
        if ($backendOut.Wait(2000)) { $backendOut.Result | Set-Content -LiteralPath "$run/backend.log" -Encoding UTF8 }
        if ($backendErr.Wait(2000)) { $backendErr.Result | Set-Content -LiteralPath "$run/backend-errors.log" -Encoding UTF8 }
        $backend.Dispose()
    }
    if ($null -ne $run) {
        try {
            $ports = @(Read-Ports)
            if ($ports.Count -gt 0) { $cleanupErrors.Add('USB/IP still reports imports. The lab did not detach imports it could not prove it owned.') }
            $ports | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$run/remaining-imports.json" -Encoding UTF8
        } catch { $cleanupErrors.Add($_.Exception.Message) }
        $passed = $null -eq $failure -and $cleanupErrors.Count -eq 0
        [pscustomobject]@{ passed = $passed; failure = $failure; cleanupErrors = @($cleanupErrors.ToArray()); observations = $rows.Count; windows = [Environment]::OSVersion.VersionString; source = $manifest.source } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$run/summary.json" -Encoding UTF8
        $text = if ($passed) { 'PASS: all Windows API readings matched; the temporary imports were removed.' } else { "INCOMPLETE/FAIL: $failure`r`nCleanup: $($cleanupErrors -join '; ')" }
        "$text`r`nSynthetic input test only. Physical controller and game compatibility are not yet validated." | Set-Content -LiteralPath "$run/REPORT.txt" -Encoding UTF8
        # Explicit allowlist: API password and per-run configuration stay local.
        $files = @(Get-ChildItem -LiteralPath $run -File | Where-Object { $_.Name -match '^(summary\.json|readings\.json|remaining-imports\.json|REPORT\.txt|backend\.log|backend-errors\.log|client-errors\.log)$' })
        Compress-Archive -LiteralPath $files.FullName -DestinationPath "$run-report.zip"
        Write-Host $text
        Write-Host "Send this report: $run-report.zip"
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
if ($null -ne $failure -or $cleanupErrors.Count -gt 0) { exit 1 }
