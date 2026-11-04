param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot/Lab.Helpers.ps1"
. "$PSScriptRoot/MultiController.Helpers.ps1"

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
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-NativeArgument "$root/Run-MultiControllerLab.ps1")
    $child = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    exit $child.ExitCode
}

$mutex = New-Object Threading.Mutex($false, 'Global\DS4WindowsGenericJoystickLab')
$locked = $false
$backend = $null; $backendOut = $null; $backendErr = $null
$client = $null; $clientErr = $null; $receipt = $null; $bus = $null
$probes = @{}; $probeErrors = @{}
$devices = @{}; $baseline = @{}
$identityIssues = New-Object 'Collections.Generic.List[string]'
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
function Stop-LabProbes {
    foreach ($architecture in @($probes.Keys)) {
        $process = $probes[$architecture]
        try {
            if (-not $process.HasExited) {
                $process.StandardInput.WriteLine('quit'); $process.StandardInput.Flush()
                if (-not $process.WaitForExit(3000)) { throw "Probe $architecture did not exit." }
            }
        } catch { $cleanupErrors.Add($_.Exception.Message) } finally {
            if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(3000) }
            if ($probeErrors[$architecture].Wait(2000)) { Add-Content -LiteralPath "$run/probe-errors.log" -Value $probeErrors[$architecture].Result }
            $process.Dispose()
        }
    }
    $probes.Clear(); $probeErrors.Clear()
}
function Capture-Pair([string]$Label, [object[]]$Expected, [switch]$EnumerationOnly) {
    Write-Host "  $Label"
    foreach ($architecture in @('x64','x86')) {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $probe = $null
            try {
                $result = Invoke-LabProcess "$root/GenericJoystickProbe-$architecture.exe" @('--json') $root 20000
                $probe = $result.Output | ConvertFrom-Json
                $problems = @(Get-MultiControllerReadingErrors $probe $Expected $baseline -EnumerationOnly:$EnumerationOnly)
                if ($result.Code -ne 0) { $problems += "Probe exit $($result.Code): $($result.Errors)" }
            } catch { $problems = @($_.Exception.Message) }
            if ($problems.Count -eq 0 -or [DateTime]::UtcNow -ge $deadline) { break }
            Start-Sleep -Milliseconds 250
        } while ($true)
        $rows.Add([pscustomobject]@{step=$Label; architecture=$architecture; expected=$Expected; observed=$probe; errors=$problems})
        $rows | ConvertTo-Json -Depth 14 | Set-Content "$run/readings.json" -Encoding UTF8
        if ($problems.Count -gt 0) {
            $otherErrors = @($problems | Where-Object { $_ -notlike 'DirectInput identity changed for controller *' })
            if ($otherErrors.Count -gt 0) { throw "$Label / $architecture : $($problems -join ' ')" }
            # Preserve identity failures, but finish the remaining diagnostics.
            # The final report still fails; no requirement is relaxed.
            $identityIssues.Add("$Label / $architecture : $($problems -join ' ')")
            Write-Host 'Identity changed; recording failure and continuing diagnostics.' -ForegroundColor Yellow
        }
        if (-not $EnumerationOnly) {
            foreach ($entry in $Expected) {
                $reading = @($probe.directInput | Where-Object {
                    $single = [pscustomobject]@{error=$false; directInput=@($_); winMM=@($_)}
                    @(Get-LabReadingErrors $single $entry.state).Count -eq 0
                })[0]
                if (-not $baseline.ContainsKey($entry.name)) { $baseline[$entry.name] = [string]$reading.instance }
            }
        }
    }
}
function Start-Device([string]$Name) {
    $state = [pscustomobject]@{process=$null; errors=$null; bus=$null; receipt=$null}
    $devices[$Name] = $state
    $options = $plan.options | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $id = if ($Name -eq 'A') { '11111111111111111111111111111111' } else { '22222222222222222222222222222222' }
    $options | Add-Member NoteProperty instanceId $id
    $optionsPath = Join-Path $run "options-$Name.json"
    $options | ConvertTo-Json -Depth 5 | Set-Content $optionsPath -Encoding ASCII
    $state.process = Start-LabProcess "$root/GenericJoystickLabClient.exe" @("--api=127.0.0.1:$apiPort", "--key-file=$keyPath", "--options-file=$optionsPath") $root
    $state.errors = $state.process.StandardError.ReadToEndAsync()
    $allocated = Read-LabReply $state.process
    if ($allocated.kind -ne 'allocated' -or [uint32]$allocated.bus -eq 0) { throw 'Invalid bus allocation.' }
    $state.bus = [uint32]$allocated.bus
    $candidate = Read-LabReply $state.process
    if ($candidate.kind -ne 'ready' -or $candidate.bus -ne $state.bus -or $candidate.port -le 0 -or $candidate.vid -ne '0xffff' -or $candidate.pid -ne '0xd10c' -or [string]$candidate.device -notmatch '^\d+$') { throw 'Invalid native attachment receipt.' }
    $state.receipt = $candidate
}
function Stop-Device([string]$Name) {
    if (-not $devices.ContainsKey($Name)) { return }
    $state = $devices[$Name]
    $script:client = $state.process; $script:clientErr = $state.errors
    $script:bus = $state.bus; $script:receipt = $state.receipt
    try {
        try { Remove-OwnedImport } catch { $cleanupErrors.Add($_.Exception.Message) }
        try { Stop-LabClient } catch { $cleanupErrors.Add($_.Exception.Message) }
    } finally {
        $script:client = $null; $script:clientErr = $null; $script:bus = $null; $script:receipt = $null
        $devices.Remove($Name)
    }
    if ($cleanupErrors.Count -gt 0) { throw 'Device cleanup failed; see report.' }
}
function Feed([string]$Name, $Step) { Send-LabCommand $devices[$Name].process $Step.wire }
function Expect([string]$Name, $State) { [pscustomobject]@{name=$Name; state=$State} }

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
    Write-Host 'Testing two independent joysticks through 32-bit and 64-bit Windows APIs.'
    Write-Host 'This uses synthetic inputs; you do not need to move your controller.'
    $backend = Start-LabProcess "$root/viiper-directinput-lab.exe" @(
        '--config-only', "--config=$configPath", '--log.level=debug', 'server',
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
    $plan = $plans[0]; $neutral = $plan.neutral
    $left = @($plan.steps | Where-Object label -eq 'Left stick left/up')[0]
    $right = @($plan.steps | Where-Object label -eq 'Right stick right/down')[0]
    $buttons = @($plan.steps | Where-Object label -eq 'All ten non-trigger buttons')[0]
    $hat = @($plan.steps | Where-Object label -eq 'Hat north-east')[0]
    $trigger = @($plan.steps | Where-Object label -eq 'Left trigger full')[0]
    Start-Device A; Start-Device B
    Capture-Pair 'Both enumerate neutral' @((Expect A $neutral),(Expect B $neutral)) -EnumerationOnly
    Send-LabCommand $devices.A.process 'reconnect'; Send-LabCommand $devices.B.process 'reconnect'
    Feed A $left; Feed B $right
    Capture-Pair 'Distinct sticks establish identities' @((Expect A $left),(Expect B $right))
    Feed A $buttons
    Capture-Pair 'A buttons preserve B sticks' @((Expect A $buttons),(Expect B $right))
    Feed A $hat
    Capture-Pair 'A hat preserves B sticks' @((Expect A $hat),(Expect B $right))
    Send-LabCommand $devices.A.process 'disconnect'
    Capture-Pair 'A producer loss neutralizes only A' @((Expect A $neutral),(Expect B $right))
    Send-LabCommand $devices.A.process 'reconnect'; Feed A $trigger
    Capture-Pair 'A producer reconnect' @((Expect A $trigger),(Expect B $right))
    Stop-Device A
    Capture-Pair 'Remove A while B remains held' @((Expect B $right))
    Start-Device A
    Capture-Pair 'Recreate A neutral' @((Expect A $neutral),(Expect B $right))
    Send-LabCommand $devices.A.process 'reconnect'; Feed A $left
    Capture-Pair 'Recreated A identity' @((Expect A $left),(Expect B $right))
    Stop-Device A; Stop-Device B
    Start-Device B; Start-Device A
    Capture-Pair 'Reverse creation order neutral' @((Expect A $neutral),(Expect B $neutral)) -EnumerationOnly
    Send-LabCommand $devices.B.process 'reconnect'; Send-LabCommand $devices.A.process 'reconnect'
    Feed B $right; Feed A $left
    Capture-Pair 'Reverse order preserves identities' @((Expect A $left),(Expect B $right))
    Stop-Device B; Stop-Device A
    if ($identityIssues.Count -gt 0) { throw "Identity persistence failed: $($identityIssues -join '; ')" }

} catch {
    $failure = $_.Exception.Message
    Write-Host "Test stopped: $failure" -ForegroundColor Red
} finally {
    foreach ($name in @($devices.Keys)) { try { Stop-Device $name } catch { $cleanupErrors.Add($_.Exception.Message) } }
    Stop-LabProbes
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
        "$text`r`nSynthetic input test only. Physical reconnects, HidHide and saved game bindings are outside this test. WinMM slots may reorder." | Set-Content -LiteralPath "$run/REPORT.txt" -Encoding UTF8
        # Explicit allowlist: API password and per-run configuration stay local.
        $files = @(Get-ChildItem -LiteralPath $run -File | Where-Object { $_.Name -match '^(summary\.json|readings\.json|remaining-imports\.json|REPORT\.txt|backend\.log|backend-errors\.log|client-errors\.log|probe-errors\.log)$' })
        Compress-Archive -LiteralPath $files.FullName -DestinationPath "$run-report.zip"
        Write-Host $text
        Write-Host "Send this report: $run-report.zip"
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
if ($null -ne $failure -or $cleanupErrors.Count -gt 0) { exit 1 }
