[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LogDirectory,
    [Parameter(Mandatory)][string]$LiveKeyPath,
    [ValidateRange(0,120)][int]$ManualSeconds = 90,
    [switch]$NonInteractive,
    [switch]$SkipLiveTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sessionId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$session = Join-Path $root ('Diagnostics\' + $sessionId)
$evidence = Join-Path $session 'evidence'
$private = Join-Path $session 'private'
$data = Join-Path $private 'lab-data'
$traceName = 'DS4W-Issue66-' + [Guid]::NewGuid().ToString('N')
$traceActive = $false
$watchdog = $null
$traceStopped = $false
$owned = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$outcomes = New-Object 'System.Collections.Generic.List[object]'
$gaps = New-Object 'System.Collections.Generic.List[string]'
$started = [DateTime]::UtcNow
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Json($Object, [string]$Name) {
    $Object | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence $Name) -Encoding UTF8
}
function Start-Owned([string]$Exe, [string[]]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Exe
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.WorkingDirectory = $root
    foreach ($name in @($info.EnvironmentVariables.Keys)) {
        if ($name.StartsWith('VIIPER_', [StringComparison]::OrdinalIgnoreCase)) { $info.EnvironmentVariables.Remove($name) }
    }
    $p = New-Object Diagnostics.Process
    $p.StartInfo = $info
    if (-not $p.Start()) { throw 'Could not start diagnostic child.' }
    $owned.Add($p)
    return $p
}
function Run-Bounded([string]$Exe, [string[]]$Arguments, [int]$Seconds = 30) {
    $p = Start-Owned $Exe $Arguments
    if (-not $p.WaitForExit($Seconds * 1000)) { $p.Kill(); [void]$p.WaitForExit(5000); throw 'Diagnostic child exceeded its deadline.' }
    return $p.ExitCode
}
function Capture-Native([string]$Name, [string]$Exe, [string[]]$Arguments) {
    try {
        $p = New-Object Diagnostics.Process
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $Exe; $info.Arguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $p.StartInfo = $info
        [void]$p.Start(); $owned.Add($p)
        $stdout = $p.StandardOutput.ReadToEndAsync(); $stderr = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'Metadata command exceeded deadline.' }
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $evidence $Name), $text, [Text.UTF8Encoding]::new($false))
        if ($p.ExitCode -ne 0) { $gaps.Add($Name + ': command exit ' + $p.ExitCode) }
        return $p.ExitCode
    } catch { $gaps.Add($Name + ': ' + $_.Exception.GetType().Name); return -1 }
}
function Save-Snapshot([string]$Suffix) {
    try {
        Write-Json @(Get-CimInstance Win32_SystemDriver | Where-Object { $_.Name -match 'usbip|hidhide|bth|tcpip|fwpkclnt|epf|eamon|edev|ndis' } |
            Select-Object Name,State,StartMode,PathName) ('drivers-' + $Suffix + '.json')
        Write-Json @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct |
            Select-Object displayName,productState,pathToSignedProductExe) ('security-products-' + $Suffix + '.json')
        Write-Json @(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -match 'Bluetooth|Wireless Controller|DualSense|USB.IP|HidHide' } |
            Select-Object DeviceName,DriverVersion,DriverDate,Manufacturer,InfName,IsSigned) ('device-drivers-' + $Suffix + '.json')
        Write-Json @(Get-Process DS4Windows,viiper -ErrorAction SilentlyContinue |
            Select-Object Id,ProcessName,StartTime,Path) ('processes-' + $Suffix + '.json')
    } catch { $gaps.Add('Snapshot ' + $Suffix + ': ' + $_.Exception.GetType().Name) }
}
function Run-Probes([string]$Label, [int]$Port) {
    $phaseStart = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        $resultPath = Join-Path $data 'probe-results.json'
        if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
        $exit = Run-Bounded (Join-Path $root 'DS4Windows.exe') @('--issue66-transport-probe',"$Port",$private)
        if ($exit -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) { throw 'Production client probe did not complete.' }
        Copy-Item -LiteralPath $resultPath -Destination (Join-Path $evidence ($Label + '-dotnet.json'))
    } catch { $gaps.Add($Label + ' .NET: ' + $_.Exception.GetType().Name) }
    $goExit = Capture-Native ($Label + '-go.json') (Join-Path $root 'extras\VIIPER-API-PROBE.exe') @('--key-file',(Join-Path $data 'viiper.key.txt'),'--port',"$Port",'--count','20')
    $outcomes.Add([ordered]@{ phase=$Label; port=$Port; startedUtc=$phaseStart; finishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); goExitCode=$goExit })
    Write-Json $outcomes.ToArray() 'phases.json'
}

try {
    # Work is confined to a fresh dedicated directory beside this extracted preview.
    foreach ($path in @($root,$LogDirectory,$LiveKeyPath)) {
        if (-not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\')) { throw 'Local absolute paths are required.' }
        for ($cursor = [IO.Path]::GetFullPath($path); $cursor; $cursor = [IO.Path]::GetDirectoryName($cursor)) {
            if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked paths are not supported.' }
        }
    }
    [void](New-Item -ItemType Directory -Path $data -Force)
    [void](New-Item -ItemType Directory -Path $evidence)
    Copy-Item -LiteralPath (Join-Path $root 'viiper.exe') -Destination (Join-Path $private 'viiper.exe')
    [IO.File]::WriteAllText((Join-Path $data 'viiper.key.txt'), 'issue66-isolated-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText((Join-Path $private 'empty.json'), '{}')
    Write-Host 'Collecting issue #66 diagnostics. Keep this window open.'
    Write-Host 'No driver, firewall, antivirus or power settings will be changed.'
    Write-Host 'The ZIP includes diagnostic logs and Windows network/system metadata. Share it privately.'
    Write-Json ([ordered]@{ session=$sessionId; startedUtc=[DateTimeOffset]::UtcNow.ToString('o'); utcOffset=[DateTimeOffset]::Now.Offset.ToString(); os=[Environment]::OSVersion.VersionString;
        elevated=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);
        processId=$PID; applicationVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'DS4Windows.exe')).ProductVersion }) 'session.json'
    Write-Json @(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $root 'DS4Windows.exe'),(Join-Path $root 'DS4Windows.dll'),(Join-Path $root 'viiper.exe'),(Join-Path $root 'extras\VIIPER-API-PROBE.exe') |
        Select-Object @{n='name';e={[IO.Path]::GetFileName($_.Path)}},Hash) 'binary-hashes.json'
    Save-Snapshot 'before'
    [void](Capture-Native 'power-plan.txt' 'powercfg.exe' @('/getactivescheme'))
    [void](Capture-Native 'usb-power-settings.txt' 'powercfg.exe' @('/query','SCHEME_CURRENT','2a737441-1930-4402-8d77-b2bebba308a3'))
    [void](Capture-Native 'network-bindings.txt' 'netcfg.exe' @('-s','n'))
    [void](Capture-Native 'winsock-catalog.txt' 'netsh.exe' @('winsock','show','catalog'))
    [void](Capture-Native 'filter-state-command.txt' 'netsh.exe' @('wfp','show','state',('file=' + (Join-Path $evidence 'filter-state.xml'))))
    # Diagnostic event metadata only; no NDIS/packet-capture provider or payload capture.
    # Do not request Packet/PiiPresent keywords; restrict to connection diagnostics. Unique bounded session, no global netsh trace.
    $providers = Join-Path $private 'providers.txt'
    [IO.File]::WriteAllLines($providers, @('{2F07E2EE-15DB-40F1-90EF-9D7BA282188A} 0x0000011400000080 5','{0C478C5B-0351-41B1-8C58-4A6737DA32E3} 0x0000011C00000000 5'), [Text.Encoding]::ASCII)
    $exit = Capture-Native 'trace-start.txt' 'logman.exe' @('create','trace',$traceName,'-ets','-o',(Join-Path $evidence 'tcpip.etl'),'-f','bincirc','-max','64',
        '-pf',$providers)
    if ($exit -ne 0) {
        # Some Windows/provider combinations reject a combined provider file.
        # Retain that error and try the core TCP provider directly instead.
        $gaps.Add('Combined TCP/filter event session unavailable; trying TCP-only fallback.')
        $exit = Capture-Native 'trace-start-fallback.txt' 'logman.exe' @('create','trace',$traceName,'-ets','-o',(Join-Path $evidence 'tcpip.etl'),'-f','bincirc','-max','64',
            '-p','{2F07E2EE-15DB-40F1-90EF-9D7BA282188A}','0x0000011400000080','5')
    }
    $traceActive = $exit -eq 0
    if ($traceActive) {
        # Direct ETW sessions do not support logman's -rf duration. An independent
        # hidden child bounds this exact GUID-named session if this window dies.
        $stopCode = "[Threading.Thread]::Sleep(480000); & '$env:SystemRoot\System32\logman.exe' stop '$traceName' -ets | Out-Null"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($stopCode))
        $watchdog = Start-Owned (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded)
    }
    if ($traceActive) { [void](Capture-Native 'trace-status.txt' 'logman.exe' @('query',$traceName,'-ets')) }
    if (-not $traceActive) { $gaps.Add('Windows TCP/filter trace unavailable; see trace-start.txt for the Windows error. Administrator permission is required.') }
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
    $listener.Start(); $testPort = $listener.LocalEndpoint.Port; $listener.Stop()
    if ($testPort -eq 3242) { throw 'Isolated endpoint collided with production port.' }
    $index = 0
    foreach ($mode in @('immediate','graceful','graceful','immediate')) {
        $index++; $label = "$index-$mode"
        Write-Host "Comparing connection shutdown: $label (both clients, 40 requests)..."
        $server = $null
        try {
            $server = Start-Owned (Join-Path $private 'viiper.exe') @('server','--config-only',('--config=' + (Join-Path $private 'empty.json')),
                ('--key-file=' + (Join-Path $data 'viiper.key.txt')),'--usb.addr=127.0.0.1:0',"--api.addr=127.0.0.1:$testPort",'--api.auto-attach-local-client=false',
                '--api.require-local-host-auth=true','--no-tray','--log.level=debug',('--log.file=' + (Join-Path $evidence ($label + '-server.log'))),
                '--usb.retained-import-authority-id=0',"--api.management-close-mode=$mode")
            $ready = $false
            for ($i=0;$i -lt 60;$i++) {
                if ($server.HasExited) { throw 'Isolated diagnostic server exited.' }
                $socket = New-Object Net.Sockets.TcpClient
                try { $socket.Connect('127.0.0.1',$testPort); $ready=$true; break } catch { Start-Sleep -Milliseconds 100 } finally { $socket.Dispose() }
            }
            if (-not $ready) { throw 'Isolated diagnostic server did not listen.' }
            Run-Probes $label $testPort
        } catch { $gaps.Add($label + ': ' + $_.Exception.GetType().Name) }
        finally { if ($server -and -not $server.HasExited) { $server.Kill(); [void]$server.WaitForExit(5000) } }
    }
    # Prove the capture can observe a known reset on an owned disposable loopback
    # connection. This does not touch the live server or a controller.
    $resetListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
    $resetClient = New-Object Net.Sockets.TcpClient
    $resetPeer = $null
    try {
        $resetListener.Start()
        $resetClient.Connect('127.0.0.1',$resetListener.LocalEndpoint.Port)
        $resetPeer = $resetListener.AcceptTcpClient()
        $resetMetadata = [ordered]@{ startedUtc=[DateTimeOffset]::UtcNow.ToString('o'); processId=$PID;
            clientPort=$resetClient.Client.LocalEndPoint.Port; serverPort=$resetPeer.Client.LocalEndPoint.Port; expectedError=10054; observedError=0 }
        $resetPeer.Client.LingerState = New-Object Net.Sockets.LingerOption($true,0)
        $resetClient.GetStream().WriteByte(65)
        [void]$resetPeer.Client.Poll(500000,[Net.Sockets.SelectMode]::SelectRead)
        $resetPeer.Client.Close(0)
        $resetClient.ReceiveTimeout = 1000
        try { [void]$resetClient.GetStream().ReadByte() } catch {
            $failure = $_.Exception
            while ($failure.InnerException) { $failure = $failure.InnerException }
            if ($failure -is [Net.Sockets.SocketException]) { $resetMetadata.observedError=$failure.NativeErrorCode }
        }
        Write-Json $resetMetadata 'controlled-reset-self-test.json'
    } finally { if ($resetPeer) { $resetPeer.Dispose() }; $resetClient.Dispose(); $resetListener.Stop() }
    if (-not $SkipLiveTests -and (Test-Path -LiteralPath $LiveKeyPath -PathType Leaf)) {
        $keyInfo = Get-Item -LiteralPath $LiveKeyPath
        if ($keyInfo.Length -gt 0 -and $keyInfo.Length -le 4096) {
            Copy-Item -LiteralPath $LiveKeyPath -Destination (Join-Path $data 'viiper.key.txt') -Force
            Write-Host 'Checking the existing controller-session server without creating or removing controllers...'
            Run-Probes '5-live-before' 3242
        } else { $gaps.Add('Live key has unexpected size; live tests skipped.') }
    } else { $gaps.Add('Live key unavailable; live tests skipped.') }
    if ($ManualSeconds -gt 0 -and -not $NonInteractive) {
        Write-Host ''
        Write-Host 'NOW: return to DS4Windows. Try Stop > Start three times and note Ready or Needs attention.'
        Write-Host "Return here and press Enter when finished, or wait $ManualSeconds seconds."
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($watch.Elapsed.TotalSeconds -lt $ManualSeconds) {
            if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'Enter') { break }
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $SkipLiveTests -and (Test-Path -LiteralPath $LiveKeyPath -PathType Leaf) -and (Get-Item -LiteralPath $LiveKeyPath).Length -le 4096) {
        Copy-Item -LiteralPath $LiveKeyPath -Destination (Join-Path $data 'viiper.key.txt') -Force
        Run-Probes '6-live-after' 3242
    }
    Save-Snapshot 'after'
    try {
        Write-Json @(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$started.AddMinutes(-10)} -MaxEvents 1000 -ErrorAction Stop |
            Where-Object { $_.ProviderName -match 'BTH|Bluetooth|Kernel-PnP|Kernel-Power|Power-Troubleshooter|TCPIP|USB' } |
            Select-Object -First 100 TimeCreated,Id,ProviderName,LevelDisplayName,Message) 'relevant-system-events.json'
    } catch { $gaps.Add('System events: ' + $_.Exception.GetType().Name) }
} catch { $gaps.Add('Collector: ' + $_.Exception.GetType().Name) }
finally {
    if ($traceActive) { $traceStopped = (Capture-Native 'trace-stop.txt' 'logman.exe' @('stop',$traceName,'-ets')) -eq 0 }
    if ($traceStopped) {
        try {
            $resets = @(Get-WinEvent -FilterHashtable @{Path=(Join-Path $evidence 'tcpip.etl');Id=1039,1040,1468,1479} -Oldest -ErrorAction Stop |
                Select-Object -First 2000 TimeCreated,Id,ProcessId,@{n='xml';e={$_.ToXml()}})
            Write-Json $resets 'tcp-reset-events.json'
            if ($resets.Count -eq 0) { $gaps.Add('No TCP reset events decoded; inspect raw tcpip.etl.') }
        } catch { $gaps.Add('TCP reset event decoding: ' + $_.Exception.GetType().Name + '; raw trace retained.') }
    }
    foreach ($p in $owned) { if (-not $traceStopped -and $watchdog -and $p.Id -eq $watchdog.Id) { continue }; try { if (-not $p.HasExited) { $p.Kill(); [void]$p.WaitForExit(5000) }; $p.Dispose() } catch {} }
    $temporaryKey = Join-Path $data 'viiper.key.txt'
    if (Test-Path -LiteralPath $temporaryKey -PathType Leaf) { Remove-Item -LiteralPath $temporaryKey }
    if (Test-Path -LiteralPath $evidence) {
        foreach ($name in @('ds4windows_log.txt','viiper-debug.log','viiper-debug-previous.log','viiper-connection-test.json')) {
            $source = Join-Path $LogDirectory $name
            try {
                if (Test-Path -LiteralPath $source -PathType Leaf) {
                    $readStream = [IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
                    try { $output = [IO.File]::Create((Join-Path $evidence $name)); try { $readStream.CopyTo($output) } finally { $output.Dispose() } } finally { $readStream.Dispose() }
                }
            } catch { $gaps.Add('Log copy ' + $name + ': ' + $_.Exception.GetType().Name) }
        }
        Write-Json ([ordered]@{ finishedUtc=[DateTimeOffset]::UtcNow.ToString('o'); gaps=$gaps.ToArray(); phaseCount=$outcomes.Count; traceStarted=$traceActive }) 'completion.json'
        [IO.File]::WriteAllText((Join-Path $evidence 'READ-ME.txt'), "Issue #66 private diagnostic bundle. Includes Windows network/filter metadata and device/system logs, which can contain account paths and network endpoints. No key files or packet payload capture are included. See completion.json for unavailable evidence. A successful run does not establish resolution.")
        # Archive only the explicit evidence directory, never the private work/key directory.
        $zip = Join-Path (Split-Path $session -Parent) ($sessionId + '.zip')
        [IO.Compression.ZipFile]::CreateFromDirectory($evidence,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
        Write-Host "Finished. Share this ZIP privately: $zip"
        Write-Host "Unavailable evidence entries: $($gaps.Count) (recorded in completion.json)."
    }
    $temporaryKey = Join-Path $data 'viiper.key.txt'
    if (Test-Path -LiteralPath $temporaryKey -PathType Leaf) { Remove-Item -LiteralPath $temporaryKey }
}
if (-not $NonInteractive) { [void](Read-Host 'Press Enter to close this diagnostics window') }
