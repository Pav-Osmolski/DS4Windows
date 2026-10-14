param([Parameter(Mandatory)][string]$PlansFile, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
# Execute the actual runner orchestration with transport/process boundaries
# replaced, not a second implementation. Never elevate or operate a driver.
$source = Get-Content "$PSScriptRoot/Run-Lab.ps1" -Raw
$source = [regex]::Replace($source, '(?s)if \(-not \[Environment\]::Is64BitOperatingSystem\).*?(?=\$mutex =)', '')
$source = $source.Replace("        Start-Sleep -Milliseconds 350", '').Replace("            Start-Sleep -Milliseconds 200", '')
$mock = @'
$script:MockAttached = $false
$script:MockForeign = $false
$script:MockDetaches = 0
$script:MockReply = 0
$script:MockConnected = $false
$script:MockExpected = $null
$script:MockNeutral = $null
$script:MockListener = $null
function Get-Process { @() }
function Test-Path {
    param([string]$LiteralPath, [string]$PathType)
    if ($LiteralPath -like '*usbip.exe') { return $true }
    if ($PathType) { Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType }
    else { Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath }
}
function New-CompletedTask([string]$Text) {
    $completion = New-Object 'Threading.Tasks.TaskCompletionSource[string]'
    $completion.SetResult($Text)
    $completion.Task
}
function Start-LabProcess([string]$File, [string[]]$Arguments, [string]$Directory) {
    $process = [pscustomobject]@{ HasExited = $false; ExitCode = 0 }
    $process | Add-Member ScriptMethod Kill { $this.HasExited = $true; if ($null -ne $script:MockListener) { $script:MockListener.Stop() } }
    $process | Add-Member ScriptMethod WaitForExit { param($Timeout) return $true }
    $process | Add-Member ScriptMethod Dispose {}
    $reader = [pscustomobject]@{}
    $reader | Add-Member ScriptMethod ReadToEndAsync { New-CompletedTask '' }
    $process | Add-Member NoteProperty StandardOutput $reader
    $process | Add-Member NoteProperty StandardError $reader
    if ($File -like '*viiper-directinput-lab.exe') {
        $api = @($Arguments | Where-Object { $_ -like '--api.addr=*' })[0]
        $apiPort = [int]($api.Split(':')[-1])
        $script:MockListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $apiPort)
        $script:MockListener.Start()
        $key = @($Arguments | Where-Object { $_ -like '--key-file=*' })[0].Substring(11)
        'PRIVATE-MOCK-KEY-MUST-NOT-BE-SHARED' | Set-Content -LiteralPath $key
    } else {
        $optionsPath = @($Arguments | Where-Object { $_ -like '--options-file=*' })[0].Substring(15)
        $options = Get-Content $optionsPath -Raw | ConvertFrom-Json
        $script:MockNeutral = [pscustomobject]@{ axes = $options.neutralAxes; buttons = 0; hat = 8 }
        $script:MockExpected = $script:MockNeutral
        $script:MockAttached = $true
        $script:MockConnected = $false
        $script:MockReply = 0
        if ($env:LAB_MOCK_SCENARIO -eq 'foreign') { $script:MockForeign = $true }
    }
    $process
}
function Read-LabReply($Process, [int]$Timeout = 15000) {
    $script:MockReply++
    if ($script:MockReply -eq 1) { return [pscustomobject]@{ kind = 'allocated'; bus = 17 } }
    if ($env:LAB_MOCK_SCENARIO -eq 'attach-failure') { throw 'Simulated stream failure after native attach.' }
    [pscustomobject]@{ kind = 'ready'; bus = 17; device = '4'; port = 7; vid = '0xffff'; pid = '0xd10c' }
}
function Send-LabCommand($Process, [string]$Command) {
    switch ($Command) {
        'quit' {
            if ($script:MockAttached) { throw 'Client stopped before owned native import was detached.' }
            $Process.HasExited = $true
        }
        'disconnect' { $script:MockConnected = $false; $script:MockExpected = $script:MockNeutral }
        'reconnect' { $script:MockConnected = $true; $script:MockExpected = $script:MockNeutral }
        default {
            if (-not $script:MockConnected) { throw 'Input was sent before the post-enumeration stream connection.' }
            $step = @($plans | ForEach-Object { $_.steps } | Where-Object { $_.wire -ceq $Command })[0]
            $script:MockExpected = $step
        }
    }
}
function Invoke-LabProcess([string]$File, [string[]]$Arguments, [string]$Directory, [int]$Timeout = 10000) {
    $output = ''
    if ($File -like '*usbip.exe') {
        switch ($Arguments[0]) {
            '--version' { $output = 'usbip (usbip-win2) 0.9.8.1' }
            'port' {
                if ($script:MockAttached) { $output = "Port 07: device in use`n -> usbip://localhost:$usbPort/17-4`n" }
                if ($script:MockForeign) { $output += "Port 08: device in use`n -> usbip://remote.example:$usbPort/99-1`n" }
            }
            'detach' {
                if ($Arguments[2] -ne '7' -or -not $script:MockAttached) { throw 'Attempted foreign or duplicate detach.' }
                $script:MockAttached = $false
                $script:MockDetaches++
            }
            default { throw 'Unexpected USB/IP operation.' }
        }
    } else {
        if ($env:LAB_MOCK_SCENARIO -eq 'probe-failure') { throw 'Simulated probe timeout.' }
        $reading = [pscustomobject]@{ axes = $script:MockExpected.axes; buttons = $script:MockExpected.buttons; hat = $script:MockExpected.hat; axisCount = 6; buttonCount = 12; hatCount = 1 }
        $output = [pscustomobject]@{ error = $false; directInput = @($reading); winMM = @($reading) } | ConvertTo-Json -Depth 6
    }
    [pscustomobject]@{ Code = 0; Output = $output; Errors = '' }
}
'@
$needle = "try {`n    `$locked = `$mutex.WaitOne(0)"
$source = $source.Replace("`r`n", "`n")
if (-not $source.Contains($needle)) { throw 'Runner structure changed: refresh this boundary test.' }
$source = $source.Replace($needle, $mock + "`n" + $needle)
foreach ($scenario in @('success', 'probe-failure', 'attach-failure', 'foreign')) {
    $directory = Join-Path $OutputDirectory $scenario
    $null = New-Item -ItemType Directory -Path $directory -Force
    $source | Set-Content -LiteralPath "$directory/Run-Lab.ps1" -Encoding UTF8
    Copy-Item -LiteralPath "$PSScriptRoot/Lab.Helpers.ps1" -Destination $directory
    Copy-Item -LiteralPath $PlansFile -Destination "$directory/plans.json"
    '{"source":{"mock":true},"files":[]}' | Set-Content -LiteralPath "$directory/manifest.json"
    $env:LAB_MOCK_SCENARIO = $scenario
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$directory/Run-Lab.ps1"
    $exitCode = $LASTEXITCODE
    $summaries = @(Get-ChildItem -LiteralPath "$directory/results" -Filter summary.json -Recurse)
    if ($summaries.Count -ne 1) { throw "Missing summary for $scenario" }
    $summary = Get-Content $summaries[0].FullName -Raw | ConvertFrom-Json
    if ($scenario -eq 'success') {
        if ($exitCode -ne 0 -or -not $summary.passed -or $summary.observations -ne 156) { throw 'Success orchestration failed.' }
    } elseif ($exitCode -eq 0 -or $summary.passed) { throw "Failure was missed: $scenario" }
    if ($scenario -ne 'foreign' -and @($summary.cleanupErrors).Count -ne 0) { throw "Owned cleanup failed: $scenario" }
    if ($scenario -eq 'foreign' -and @($summary.cleanupErrors).Count -eq 0) { throw 'Foreign import was silently ignored.' }
    $archive = @(Get-ChildItem -LiteralPath "$directory/results" -Filter '*-report.zip')[0]
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archive.FullName)
    try {
        if (@($zip.Entries | Where-Object { $_.Name -like '*key*' -or $_.Name -like '*config*' }).Count -gt 0) { throw 'Private files leaked into report.' }
    } finally { $zip.Dispose() }
}
Remove-Item Env:LAB_MOCK_SCENARIO
Write-Host 'Runner success, probe timeout, post-attach failure, foreign-import preservation and report privacy checks passed.'
