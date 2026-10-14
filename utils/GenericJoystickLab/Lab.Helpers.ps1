Set-StrictMode -Version Latest

function ConvertTo-NativeArgument([string]$Value) {
    # CommandLineToArgvW quoting, including trailing backslashes; PS 5.1 compatible.
    '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}

function Start-LabProcess([string]$File, [string[]]$Arguments, [string]$Directory) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $File
    $info.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $info.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    # Never inherit configuration or developer startup policy into this backend.
    foreach ($name in @($info.EnvironmentVariables.Keys)) {
        if ([string]$name -like 'VIIPER_*') { $info.EnvironmentVariables.Remove([string]$name) }
    }
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    if (-not $process.Start()) { throw "Could not start $File" }
    $process
}

function Invoke-LabProcess([string]$File, [string[]]$Arguments, [string]$Directory, [int]$Timeout = 10000) {
    $process = Start-LabProcess $File $Arguments $Directory
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($Timeout)) { throw "Timed out: $File" }
        if (-not $output.Wait(2000) -or -not $errors.Wait(2000)) { throw "Timed out reading: $File" }
        [pscustomobject]@{ Code = $process.ExitCode; Output = $output.Result; Errors = $errors.Result }
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(3000) }
        $process.Dispose()
    }
}

function Read-LabReply($Process, [int]$Timeout = 15000) {
    $task = $Process.StandardOutput.ReadLineAsync()
    if (-not $task.Wait($Timeout)) { throw 'Lab client response timed out.' }
    if ($null -eq $task.Result) { throw 'Lab client exited before replying.' }
    $reply = $task.Result | ConvertFrom-Json
    if ($reply.kind -eq 'error') { throw "Lab client: $($reply.message)" }
    $reply
}

function Send-LabCommand($Process, [string]$Command) {
    $Process.StandardInput.WriteLine($Command)
    $Process.StandardInput.Flush()
    $reply = Read-LabReply $Process 5000
    $kind = if ($Command -eq 'quit') { 'stopped' } else { 'ok' }
    if ($reply.kind -ne $kind) { throw "Unexpected client reply to $Command" }
    if ($kind -eq 'ok' -and $reply.command -cne $Command) { throw 'Client command acknowledgement did not match.' }
}

function Get-LabPorts([string]$Text) {
    $blocks = [regex]::Matches($Text, '(?ms)^\s*Port\s+(\d+):.*?(?=^\s*Port\s+\d+:|\z)')
    $seen = @{}
    foreach ($block in $blocks) {
        $urls = [regex]::Matches($block.Value, '(?m)^\s*->\s*usbip://([^\s]+)\s*$')
        $port = [int]$block.Groups[1].Value
        if ($seen.ContainsKey($port)) { throw "Duplicate USB/IP import at port $port" }
        $seen[$port] = $true
        if ($urls.Count -ne 1) { throw "Ambiguous USB/IP import at port $port" }
        $uri = $null
        if (-not [uri]::TryCreate(('usbip://' + $urls[0].Groups[1].Value), [UriKind]::Absolute, [ref]$uri)) {
            throw "Invalid USB/IP import at port $port"
        }
        if ($uri.Port -le 0 -or $uri.UserInfo -ne '' -or $uri.Query -ne '' -or $uri.Fragment -ne '') { throw "Ambiguous USB/IP location at port $port" }
        [pscustomobject]@{ Port = $port; Host = $uri.Host; ServerPort = $uri.Port; Device = $uri.AbsolutePath.TrimStart('/') }
    }
}

function Test-LabPortOwner($Port, [int]$ServerPort, [uint32]$Bus, $Receipt) {
    if ($Port.Host -cne 'localhost' -or $Port.ServerPort -ne $ServerPort) { return $false }
    if ($Port.Device -notmatch '^(\d+)-(\d+)$' -or [uint32]$Matches[1] -ne $Bus) { return $false }
    if ($null -ne $Receipt) {
        return $Port.Port -eq [int]$Receipt.port -and $Port.Device -ceq "$Bus-$($Receipt.device)"
    }
    # The allocated bus event precedes device creation; this also covers attach
    # success followed by stream failure, when no final receipt was returned.
    return $true
}

function Get-LabReadingErrors($Probe, $Expected) {
    if ($Probe.error) { 'The native probe reported an API error.' }
    foreach ($api in @('directInput', 'winMM')) {
        $readings = @($Probe.$api)
        if ($readings.Count -ne 1) { "$api found $($readings.Count) devices; expected exactly one."; continue }
        $reading = $readings[0]
        if ($reading.axisCount -ne 6 -or $reading.buttonCount -ne 12 -or $reading.hatCount -ne 1) {
            "$api capabilities differ from six axes, twelve buttons and one hat."
        }
        if (@($reading.axes).Count -ne 6) { "$api did not return six axis values."; continue }
        for ($i = 0; $i -lt 6; $i++) {
            # WinMM integer scaling may round the centre by one unit.
            if ([math]::Abs([int]$reading.axes[$i] - [int]$Expected.axes[$i]) -gt 3) {
                "$api axis $i was $($reading.axes[$i]); expected $($Expected.axes[$i])."
            }
        }
        if ($reading.buttons -ne $Expected.buttons) { "$api buttons were $($reading.buttons); expected $($Expected.buttons)." }
        if ($reading.hat -ne $Expected.hat) { "$api hat was $($reading.hat); expected $($Expected.hat)." }
    }
}
