param([string]$BackendScript = "$PSScriptRoot\..\extras\install-viiper-backend.ps1")
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $BackendScript), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Test-UsbipDeviceIsPresent','Get-UsbipCleanRepairPlan',
        'Assert-UsbipControllersEmpty','Invoke-UsbipPnPRemoval','Invoke-UsbipResidualCleanup')) {
    $node = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}, $true)
    if (-not $node) { throw "Missing function: $name" }
    Invoke-Expression $node.Extent.Text
}
# Compile the native tree reader, but NEVER invoke it against the test host.
$definition = $ast.Find({param($n) $n -is [Management.Automation.Language.StringConstantExpressionAst] -and $n.Value.Contains('public static class UsbipDeviceTree')}, $true)
if (-not $definition) { throw 'Missing native tree reader' }
Add-Type -TypeDefinition $definition.Value

function Root([string]$id) {
    [pscustomobject]@{ Service='usbip2_ude'; PNPDeviceID=$id;
        HardwareID=@('ROOT\USBIP_WIN2\UDE'); Present=$true; ConfigManagerErrorCode=0 }
}
function Package([string]$name, [string]$inf) {
    [pscustomobject]@{Driver=$name;OriginalFileName="C:\Windows\INF\$inf";ProviderName='USBIP-WIN2';Inbox=$false}
}
function Reset-Fixture {
    $script:Devices = @((Root 'ROOT\USB\0000'), (Root 'ROOT\USB\0001'))
    $script:Packages = @((Package 'oem20.inf' 'usbip2_ude.inf'),
        (Package 'oem21.inf' 'usbip2_ude.inf'), (Package 'oem22.inf' 'usbip2_filter.inf'))
    $script:Children = @()
    $script:InspectionFailure = $false
    $script:Changes = [Collections.Generic.List[string]]::new()
    $script:Boundary = $null
    $script:NativeExit = 0
    $script:RebootBoundaryPending = $false
    $script:StopAllowed = $true
    $script:PlanCalls = 0
    $script:NewImportAt = 0
}
function Get-CimInstance {
    param($ClassName, $ErrorAction)
    if ($ClassName -ne 'Win32_PnPEntity') { throw 'Unexpected host query' }
    $script:PlanCalls++
    if ($script:InspectionFailure) { throw 'Mock inspection failure' }
    if ($script:NewImportAt -and $script:PlanCalls -ge $script:NewImportAt) {
        $script:Children = @('USB\VID_1234&PID_5678\FOREIGN')
    }
    return $script:Devices
}
function Get-WindowsDriver { param([switch]$Online,[switch]$All,$ErrorAction) return $script:Packages }
function Get-UsbipDeviceDescendantIds { param($instanceId) return $script:Children }
function Stop-Ds4WindowsProcesses { param($operation) return $script:StopAllowed }
function Stop-ViiperProcesses { param($operation) return $script:StopAllowed }
function Get-WindowsBootSessionId { return 'after-official-uninstall' }
function Set-Content {
    param([Parameter(ValueFromPipeline=$true)]$Value,$LiteralPath,$Encoding)
    process {
        if ($LiteralPath -ne 'MockUsbip:\boundary.json') { throw 'Unexpected state write' }
        $script:Boundary = $Value | ConvertFrom-Json
    }
}
function Write-SetupLog { param($message,$color) }
function Invoke-TestPnP {
    if (-not $script:Boundary -or -not $script:Boundary.CleanupAttempted) { throw 'Mutation before durable boundary' }
    $script:Changes.Add(($args -join ' '))
    $script:LASTEXITCODE = $script:NativeExit
    $target = $args[1]
    if ($script:NativeExit -eq 0 -or $script:NativeExit -eq 3010) {
        if ($args[0] -eq '/remove-device') {
            $script:Devices = @($script:Devices | Where-Object { $_.PNPDeviceID -ne $target })
        } elseif ($args[0] -eq '/delete-driver') {
            $script:Packages = @($script:Packages | Where-Object { $_.Driver -ne $target })
        } else { throw 'Unexpected fake native operation' }
    }
}
$script:PnPUtilPath = 'Invoke-TestPnP'
$script:UsbipReplacementStatePath = 'MockUsbip:\boundary.json'
function Attempt-Cleanup {
    param($state = ([pscustomobject]@{BootSessionId='previous-boot'}))
    try { Invoke-UsbipResidualCleanup $state; throw 'Cleanup unexpectedly returned' }
    catch { return $_.Exception.Message }
}
function Assert-NoChange([string]$scenario) {
    if ($script:Changes.Count -or $script:Boundary) { throw "Mutation in rejected scenario: $scenario" }
}

Reset-Fixture
$message = Attempt-Cleanup
if ($script:Changes.Count -ne 5 -or -not $script:RebootBoundaryPending -or
        $script:Devices.Count -or $script:Packages.Count -or $message -notmatch 'Restart Windows') {
    throw "Duplicate cleanup failed: changes=$($script:Changes.Count); $message"
}
if (($script:Changes -join ' ') -match '/force|System32|/disable-device') { throw 'Unsafe cleanup operation' }

Reset-Fixture
$hub = [pscustomobject]@{Service='USBHUB3';PNPDeviceID='USB\ROOT_HUB30\HUB';HardwareID=@('USB\ROOT_HUB30&VID1234');Present=$true}
$script:Devices += $hub; $script:Children = @($hub.PNPDeviceID)
[void](Get-UsbipCleanRepairPlan) # Empty infrastructural root hubs are allowed.

foreach ($scenario in @('foreign-child','missing-hwid','wrong-service','wrong-provider',
        'forged-oem-name','inbox-package','inspection-failure','stop-failure','already-attempted')) {
    Reset-Fixture
    switch ($scenario) {
        'foreign-child' { $script:Children = @('USB\VID_1234&PID_5678\FOREIGN') }
        'missing-hwid' { $script:Devices[0].HardwareID = @('ROOT\OTHER_DEVICE') }
        'wrong-service' { $script:Devices[0].Service = 'foreign-driver' }
        'wrong-provider' { $script:Packages[0].ProviderName = 'Unrelated Vendor' }
        'forged-oem-name' { $script:Packages[0].Driver = '..\unrelated.inf' }
        'inbox-package' { $script:Packages[0].Inbox = $true }
        'inspection-failure' { $script:InspectionFailure = $true }
        'stop-failure' { $script:StopAllowed = $false }
    }
    if ($scenario -eq 'already-attempted') {
        $message = Attempt-Cleanup ([pscustomobject]@{BootSessionId='previous-boot';CleanupAttempted=$true})
        if ($message -notmatch 'will not repeat') { throw 'Cleanup retry is unbounded' }
    } else { $message = Attempt-Cleanup }
    Assert-NoChange $scenario
}

Reset-Fixture
$script:NativeExit = 5
$message = Attempt-Cleanup
if ($script:Changes.Count -ne 1 -or -not $script:Boundary.CleanupAttempted -or
        $script:RebootBoundaryPending -or $message -notmatch 'exit=5') {
    throw 'Native failure did not stop with preserved diagnostic state.'
}

Reset-Fixture
$script:NewImportAt = 2
$message = Attempt-Cleanup
if ($script:Changes.Count -or -not $script:Boundary.CleanupAttempted -or $message -notmatch 'unclassified child') {
    throw 'A new imported device was not detected before removal.'
}

Reset-Fixture
$script:Packages += Package 'oem99.inf' 'another_usbip2_ude.inf'
$plan = Get-UsbipCleanRepairPlan
if ($plan.Packages.Count -ne 3) { throw 'Unrelated similarly named INF selected.' }
$script:Devices = @()
try { Assert-UsbipControllersEmpty -RequireController; throw 'Empty duplicate inventory admitted' }
catch { if ($_.Exception.Message -notmatch 'no exact controller') { throw } }

Write-Host 'USB/IP cleanup: exact identities, duplicate roots/old packages, empty hubs, foreign children, inspection/stop/native failures, changed inventory and bounded retry passed. No host devices or drivers were changed.'

# Exercise the real boundary coordinator across both required restarts. All
# state/device/native operations remain mocks; the installer is never run.
$node = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Resolve-UsbipReplacementBoundary'}, $true)
Invoke-Expression $node.Extent.Text
function Test-Path { param($LiteralPath) return $true }
function Get-Content { param($LiteralPath,[switch]$Raw) return ($script:Boundary | ConvertTo-Json) }
function Remove-Item { param($LiteralPath,[switch]$Force) $script:BoundaryRemoved = $true }
function Get-WindowsBootSessionId { return $script:Boot }
function Assert-UsbipPostRebootState {
    if ($script:Devices.Count -or $script:Packages.Count) { throw 'Verified USB/IP leftovers remain' }
}
Reset-Fixture
$script:Boundary = [pscustomobject]@{BootSessionId='before-uninstall'}
$script:Boot = 'after-uninstall'; $script:BoundaryRemoved = $false
try { Resolve-UsbipReplacementBoundary; throw 'Cleanup did not request restart' }
catch { if ($_.Exception.Message -notmatch 'Restart Windows') { throw } }
if ($script:Changes.Count -ne 5 -or $script:BoundaryRemoved) { throw 'Boundary was cleared prematurely' }
$script:Changes.Clear()
try { Resolve-UsbipReplacementBoundary; throw 'Same-boot continuation admitted' }
catch { if ($_.Exception.Message -notmatch 'Restart Windows') { throw } }
if ($script:Changes.Count) { throw 'Same-boot cleanup repeated' }
$script:Boot = 'after-cleanup'; $script:RebootBoundaryPending = $false
Resolve-UsbipReplacementBoundary
if (-not $script:BoundaryRemoved -or $script:RebootBoundaryPending) { throw 'Verified post-cleanup continuation was not admitted' }
Write-Host 'Official uninstall -> restart -> residual cleanup -> restart -> verified continuation passed.'
