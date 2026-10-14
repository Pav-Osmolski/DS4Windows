[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackendScript
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$backendPath = (Resolve-Path -LiteralPath $BackendScript).Path
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $backendPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Backend installer has PowerShell parse errors: " +
        (($parseErrors | ForEach-Object Message) -join "; ")
}

function Get-BackendFunctionDefinition([string]$name) {
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $name
    }, $true)
    if (-not $definition) {
        throw "Backend installer function is missing: $name"
    }
    return $definition.Extent.Text
}

# Import the literal needed by the extracted startup functions without running
# the installer's top-level code (which would touch real machine state).
$argumentAssignments = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'script:ViiperServerArguments'
}, $true))
if ($argumentAssignments.Count -ne 1 -or
        $argumentAssignments[0].Right -isnot [Management.Automation.Language.CommandExpressionAst] -or
        $argumentAssignments[0].Right.Expression -isnot [Management.Automation.Language.StringConstantExpressionAst]) {
    throw 'Backend startup arguments must have one literal definition.'
}
$script:ViiperServerArguments = $argumentAssignments[0].Right.Expression.Value
if ($script:ViiperServerArguments -ne
        'server --usb.retained-import-authority-id=4923336367393615921') {
    throw 'Backend startup arguments lost the required retained-import authority.'
}

foreach ($functionName in @(
        "ConvertTo-VersionFromObject",
        "Set-UsbipReplacementBoundary",
        "Remove-MismatchedUsbipPackage",
        "Resolve-UsbipReplacementBoundary",
        "Test-Ds4WindowsOwnedUsbipPort",
        "Suspend-StartupTasksUntilInfrastructureReady",
        "Set-InfrastructureStartupFailClosed")) {
    Invoke-Expression (Get-BackendFunctionDefinition $functionName)
}

# Parse the new upstream port output and the older quoted form without running
# any installer top-level code or touching the installed USB/IP driver.
foreach ($case in @(
        @{ Line = '-> serial: '; Owned = $true },
        @{ Line = '-> serial: DS4W0123456789A'; Owned = $true },
        @{ Line = '-> serial: foreign'; Owned = $false },
        @{ Line = "-> serial: 'foreign'"; Owned = $false },
        @{ Line = "-> serial ''"; Owned = $true },
        @{ Line = "-> serial 'DS4W0123456789A'"; Owned = $true },
        @{ Line = "-> serial 'foreign'"; Owned = $false },
        @{ Line = '-> serial foreign'; Owned = $false })) {
    foreach ($location in @('localhost:3241/17-4', 'remote.example:3241/17-4',
            'localhost:3240/17-4', 'localhost:3241/x1-protected')) {
        $block = "Port 07: device in use at high-speed`n -> usbip://$location`n $($case.Line)`n -> mode: low-latency"
        $expected = $case.Owned -and $location -eq 'localhost:3241/17-4'
        if ((Test-Ds4WindowsOwnedUsbipPort $block) -ne $expected) {
            throw "USB-IP cleanup ownership mismatch: $location / $($case.Line)"
        }
    }
}
Write-Host "USB-IP cleanup rejects foreign imports in both port-output formats."

$testRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "DS4Windows-Usbip-Reboot-Test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $usbipRoot = Join-Path $testRoot "USBip"
    New-Item -ItemType Directory -Path $usbipRoot | Out-Null
    $script:CanonicalUsbipPath = Join-Path $usbipRoot "usbip.exe"
    $uninstaller = Join-Path $usbipRoot "unins000.exe"
    [IO.File]::WriteAllBytes($script:CanonicalUsbipPath, [byte[]](0))
    [IO.File]::WriteAllBytes($uninstaller, [byte[]](0))
    $script:UsbipReplacementStatePath = Join-Path $testRoot `
        "usbip-replacement-pending.json"
    $script:InstallerLogRoot = Join-Path $testRoot "Installer logs"
    $script:UsbipReplacementPhaseOne = $false
    $script:RebootRecommended = $false
    $script:UsbipRuntimeReady = $true
    $script:RebootBoundaryPending = $false
    $script:CurrentTestBoot = "boot-a"
    $script:PostRebootChecks = 0
    $script:FakeUninstallExitCode = 0

    function Get-WindowsBootSessionId {
        return $script:CurrentTestBoot
    }
    function Assert-UsbipPostRebootState {
        $script:PostRebootChecks++
    }
    function Write-SetupLog {
        param([string]$message, $color)
    }
    $script:FakeStartupTasks = @{
        RunVIIPER = $true
        RunDS4Windows = $true
    }
    function Test-HighestLogonTask {
        param(
            [string]$taskName,
            [string]$executablePath,
            [string]$arguments,
            [string]$workingDirectory,
            [bool]$requireEnabled = $true
        )
        return $script:FakeStartupTasks.ContainsKey($taskName) -and
            (-not $requireEnabled -or
            $script:FakeStartupTasks[$taskName])
    }
    function Test-ManagedStartupTaskMarker {
        param($registered)
        return $null -ne $registered
    }
    function Test-ManagedStartupTaskOwnership {
        param($registered, [string]$taskName)
        return $null -ne $registered
    }
    function Save-ManagedStartupTaskBackup {
        param($registered, [string]$taskName, [switch]$IncludeMarked)
        if (-not $registered -or -not $IncludeMarked) {
            throw "Reboot containment must preserve the marked task before disabling it."
        }
    }
    function Test-HighestLogonTaskDefinition {
        return $true
    }
    function Get-RootScheduledTask {
        param([string]$taskName)
        if (-not $script:FakeStartupTasks.ContainsKey($taskName)) {
            return $null
        }
        return [pscustomobject]@{
            TaskPath = "\"
            TaskName = $taskName
            Description = "DS4Windows managed startup task v1"
            Settings = [pscustomobject]@{
                Enabled = $script:FakeStartupTasks[$taskName]
            }
        }
    }
    function Disable-ScheduledTask {
        param(
            [string]$TaskPath,
            [string]$TaskName,
            $ErrorAction
        )
        $script:FakeStartupTasks[$TaskName] = $false
    }
    function Get-ScheduledTask {
        param(
            [string]$TaskPath,
            [string]$TaskName,
            $ErrorAction
        )
        return [pscustomobject]@{
            Settings = [pscustomobject]@{
                Enabled = $script:FakeStartupTasks[$TaskName]
            }
        }
    }
    function Start-Process {
        param(
            [string]$FilePath,
            [object]$ArgumentList,
            [string]$WindowStyle,
            [switch]$PassThru
        )
        $script:CapturedUninstallArguments = [string]$ArgumentList
        if ($WindowStyle -ne 'Hidden') {
            throw "USB-IP uninstaller must not open an interactive window."
        }
        if (-not [string]::Equals((Resolve-Path -LiteralPath $FilePath).Path,
                (Resolve-Path -LiteralPath $uninstaller).Path,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Replacement test attempted an unexpected executable."
        }
        $process = [pscustomobject]@{
            ExitCode = $script:FakeUninstallExitCode
        }
        $process | Add-Member ScriptMethod WaitForExit {
            param([int]$milliseconds)
            return $true
        }
        $process | Add-Member ScriptMethod Refresh { }
        return $process
    }

    $entry = [pscustomobject]@{
        DisplayVersion = "0.9.7.7"
        QuietUninstallString = '"' + $uninstaller + '"'
        UninstallString = $null
    }
    $removed = Remove-MismatchedUsbipPackage $entry `
        ([Version]"0.9.7.7") ([Version]"0.9.8.1")
    if (-not $removed -or -not $script:UsbipReplacementPhaseOne -or
            -not $script:RebootRecommended -or
            $script:UsbipRuntimeReady) {
        throw "0.9.7.7 phase-one replacement contract did not converge."
    }

    $state = Get-Content -LiteralPath $script:UsbipReplacementStatePath `
        -Raw | ConvertFrom-Json
    if ($state.BootSessionId -ne "boot-a" -or
            $state.RemovedVersion -ne "0.9.7.7" -or
            $state.RequiredVersion -ne "0.9.8.1") {
        throw "USB-IP replacement marker did not preserve exact versions."
    }
    if (-not $state.UninstallLogPath.StartsWith($script:InstallerLogRoot) -or
            -not $script:CapturedUninstallArguments.Contains(
                '/LOG="' + $state.UninstallLogPath + '"') -or
            -not $script:CapturedUninstallArguments.Contains('/NORESTART')) {
        throw "USB-IP uninstall diagnostics must survive reboot with a quoted log path."
    }
    $firstUninstallLogPath = $state.UninstallLogPath

    $sameBootRejected = $false
    try {
        Resolve-UsbipReplacementBoundary
    }
    catch {
        $sameBootRejected = $_.Exception.Message -match
            "Restart Windows before"
    }
    if (-not $sameBootRejected -or
            -not $script:RebootBoundaryPending -or
            -not (Test-Path -LiteralPath $script:UsbipReplacementStatePath)) {
        throw "USB-IP replacement crossed its same-boot safety boundary."
    }

    $script:CurrentTestBoot = "boot-b"
    $script:RebootBoundaryPending = $false
    Resolve-UsbipReplacementBoundary
    if ($script:PostRebootChecks -ne 1 -or
            (Test-Path -LiteralPath $script:UsbipReplacementStatePath)) {
        throw "USB-IP replacement did not validate and clear after reboot."
    }

    Suspend-StartupTasksUntilInfrastructureReady `
        (Join-Path $testRoot "viiper.exe") `
        (Join-Path $testRoot "DS4Windows.exe")
    if ($script:FakeStartupTasks.RunVIIPER -or
            $script:FakeStartupTasks.RunDS4Windows) {
        throw "Startup tasks could race phase-two USB-IP setup after reboot."
    }

    $script:FakeStartupTasks.RunVIIPER = $true
    $script:FakeStartupTasks.RunDS4Windows = $true
    Set-InfrastructureStartupFailClosed `
        (Join-Path $testRoot "viiper.exe") `
        (Join-Path $testRoot "DS4Windows.exe")
    if ($script:FakeStartupTasks.RunVIIPER -or
            $script:FakeStartupTasks.RunDS4Windows) {
        throw "Failed setup could leave an owned startup task enabled."
    }

    $backendText = Get-Content -LiteralPath $backendPath -Raw
    if ([regex]::Matches($backendText,
            'Suspend-StartupTasksForSetup').Count -lt 3) {
        throw "Both reboot-pending branches must suspend startup tasks."
    }

    # A failed uninstaller must never leave a marker that authorizes phase two.
    $script:CurrentTestBoot = "boot-c"
    $script:FakeUninstallExitCode = 5
    $script:UsbipReplacementPhaseOne = $false
    $failedUninstallRejected = $false
    try {
        [void](Remove-MismatchedUsbipPackage $entry `
            ([Version]"0.9.7.7") ([Version]"0.9.8.1"))
    }
    catch {
        $failedUninstallRejected = $_.Exception.Message -match "exit code 5"
    }
    if (-not $failedUninstallRejected -or
            (Test-Path -LiteralPath $script:UsbipReplacementStatePath)) {
        throw "A failed USB-IP uninstall could authorize a later phase."
    }
    if ($script:CapturedUninstallArguments.Contains($firstUninstallLogPath)) {
        throw "A later uninstall must not overwrite the earlier diagnostic log."
    }

    Write-Host "USB-IP 0.9.7.7 -> reboot -> 0.9.8.1 state simulation passed."
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force `
        -ErrorAction SilentlyContinue
}

# Exercise the real post-reboot inspection with fully mocked Windows/native
# queries. Do not dot-source the installer or mutate drivers on the test host.
& {
    Invoke-Expression (Get-BackendFunctionDefinition 'Test-UsbipDeviceIsPresent')
    Invoke-Expression (Get-BackendFunctionDefinition 'Assert-UsbipPostRebootState')
    $script:PnPUtilPath = 'Invoke-TestDriverStoreQuery'
    $script:TestDeviceRecords = @()
    $script:TestDriverRecords = @()
    $script:TestDriverStoreOutput = 'No third-party driver packages.'
    $script:TestDriverStoreExitCode = 0
    $script:TestQueryFailure = $false
    $script:TestDriverStoreCalls = 0
    $script:TestPostRebootMessages = [Collections.Generic.List[string]]::new()

    function Get-CimInstance {
        param([string]$ClassName, [string]$ErrorAction)
        if ($script:TestQueryFailure) { throw 'Mock WMI query failed' }
        switch ($ClassName) {
            'Win32_PnPEntity' { return $script:TestDeviceRecords }
            'Win32_SystemDriver' { return $script:TestDriverRecords }
            default { throw "Unexpected live query: $ClassName" }
        }
    }
    function Write-SetupLog {
        param([string]$message, $color)
        $script:TestPostRebootMessages.Add($message)
    }
    function Invoke-TestDriverStoreQuery {
        param([string]$operation)
        if ($operation -ne '/enum-drivers') { throw 'Unexpected native mutation' }
        $script:TestDriverStoreCalls++
        $script:LASTEXITCODE = $script:TestDriverStoreExitCode
        return $script:TestDriverStoreOutput
    }

    $cases = @(
        @{ Name='No remaining device'; Device=$null; Allowed=$true },
        @{ Name='Explicitly non-present record'; Device=@{Present=$false;ConfigManagerErrorCode=0};Allowed=$true },
        @{ Name='Phantom with unknown Present'; Device=@{ConfigManagerErrorCode=45};Allowed=$true },
        @{ Name='Phantom with null Present'; Device=@{Present=$null;ConfigManagerErrorCode=45};Allowed=$true },
        @{ Name='Present overrides contradictory phantom code'; Device=@{Present=$true;ConfigManagerErrorCode=45};Allowed=$false },
        @{ Name='Unknown presence fails closed'; Device=@{ConfigManagerErrorCode=0};Allowed=$false },
        @{ Name='Null presence fails closed'; Device=@{Present=$null;ConfigManagerErrorCode=0};Allowed=$false },
        @{ Name='Disabled is still present'; Device=@{Present=$true;ConfigManagerErrorCode=22};Allowed=$false },
        @{ Name='Driverless is still present'; Device=@{Present=$true;ConfigManagerErrorCode=28};Allowed=$false },
        @{ Name='Phantom cannot bypass running UDE'; Device=@{Present=$false;ConfigManagerErrorCode=45};Driver='usbip2_ude';Allowed=$false },
        @{ Name='Phantom cannot bypass running filter'; Device=@{Present=$false;ConfigManagerErrorCode=45};Driver='usbip2_filter';Allowed=$false },
        @{ Name='Phantom cannot bypass retained UDE package'; Device=@{Present=$false;ConfigManagerErrorCode=45};Package='usbip2_ude.inf';Allowed=$false },
        @{ Name='Phantom cannot bypass retained filter package'; Device=@{Present=$false;ConfigManagerErrorCode=45};Package='usbip2_filter.inf';Allowed=$false },
        @{ Name='DriverStore query failure fails closed'; Device=$null;NativeExit=1;Allowed=$false },
        @{ Name='WMI failure fails closed'; Device=$null;QueryFailure=$true;Allowed=$false }
    )
    foreach ($case in $cases) {
        $script:TestDeviceRecords = @()
        if ($null -ne $case.Device) {
            $record = @{Service='usbip2_ude';PNPDeviceID='ROOT\USBIP_WIN2\0000';Status='Mock'}
            foreach ($key in $case.Device.Keys) { $record[$key] = $case.Device[$key] }
            $script:TestDeviceRecords = @([pscustomobject]$record)
        }
        $script:TestDriverRecords = @()
        if ($case.ContainsKey('Driver')) {
            $script:TestDriverRecords = @([pscustomobject]@{Name=$case.Driver;State='Running'})
        }
        $script:TestDriverStoreOutput = if ($case.ContainsKey('Package')) { $case.Package } else { 'No packages.' }
        $script:TestDriverStoreExitCode = if ($case.ContainsKey('NativeExit')) { $case.NativeExit } else { 0 }
        $script:TestQueryFailure = $case.ContainsKey('QueryFailure')
        $script:TestDriverStoreCalls = 0
        $script:TestPostRebootMessages.Clear()
        $allowed = $true
        try { Assert-UsbipPostRebootState } catch { $allowed = $false }
        if ($allowed -ne $case.Allowed) { throw "Post-reboot policy failed: $($case.Name)" }
        if ($allowed -and $script:TestDriverStoreCalls -ne 1) {
            throw "DriverStore inspection was skipped: $($case.Name)"
        }
        if ($null -ne $case.Device -and
            -not ($script:TestPostRebootMessages -match 'PNPDeviceID=ROOT')) {
            throw "Missing device diagnostics: $($case.Name)"
        }
    }
    # Mock the boundary file too: exercise the real resolver without writing
    # or deleting installer state on the host.
    $script:UsbipReplacementStatePath = 'MockBoundary:\usbip-replacement.json'
    $script:TestBoundaryExists = $true
    function Test-Path {
        param([string]$LiteralPath)
        if ($LiteralPath -ne $script:UsbipReplacementStatePath) {
            throw 'Unexpected filesystem query'
        }
        return $script:TestBoundaryExists
    }
    function Get-Content {
        param([string]$LiteralPath, [switch]$Raw)
        if ($LiteralPath -ne $script:UsbipReplacementStatePath) {
            throw 'Unexpected state read'
        }
        return '{"BootSessionId":"boot-a"}'
    }
    function Remove-Item {
        param([string]$LiteralPath, [switch]$Force)
        if ($LiteralPath -ne $script:UsbipReplacementStatePath -or -not $Force) {
            throw 'Unexpected state removal'
        }
        $script:TestBoundaryExists = $false
    }
    function Get-WindowsBootSessionId { return 'boot-b' }
    $script:TestQueryFailure = $false
    $script:TestDriverStoreExitCode = 0
    $script:TestDriverStoreOutput = 'No packages.'
    $script:TestDriverRecords = @()
    $script:TestDeviceRecords = @([pscustomobject]@{
        Service='usbip2_ude';PNPDeviceID='ROOT\USBIP_WIN2\0000';Present=$true;ConfigManagerErrorCode=0;Status='Mock'
    })
    $rejected = $false
    try { Resolve-UsbipReplacementBoundary } catch { $rejected = $true }
    if (-not $rejected -or -not $script:TestBoundaryExists) {
        throw 'An unsafe inspection must retain its reboot boundary.'
    }
    $script:TestDeviceRecords[0].Present = $false
    $script:TestDeviceRecords[0].ConfigManagerErrorCode = 45
    Resolve-UsbipReplacementBoundary
    if ($script:TestBoundaryExists) {
        throw 'A fully verified inspection must clear its boundary.'
    }
    Write-Host "USB-IP post-reboot inspection passed $($cases.Count) presence, driver and DriverStore cases plus boundary retention/recovery."
}
