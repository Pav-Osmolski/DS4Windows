<#
.SYNOPSIS
Queries the opt-in local DSX listener. No effects are sent unless -TestEffects is supplied.
.DESCRIPTION
Enable Game mod support (DSX) in Settings and press Start first. ControllerIndex
is the zero-based DS4Windows slot, not the position among connected DualSense pads.
-TestEffects briefly changes the selected DualSense's light and right trigger,
then sends ResetToUserSettings to release the overrides. No profile is edited.
Do not use the effect test while playing. Trigger Lab has priority per trigger.
#>
[CmdletBinding()]
param(
    [string]$ServerIp = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 6969,
    [ValidateRange(0, 7)][int]$ControllerIndex = 0,
    [switch]$TestEffects
)

$targetAddress = [System.Net.IPAddress]::Parse($ServerIp)
if (-not [System.Net.IPAddress]::IsLoopback($targetAddress)) {
    throw 'Only a listener on this PC is supported.'
}
$udpClient = [System.Net.Sockets.UdpClient]::new($targetAddress.AddressFamily)
$udpClient.Client.ReceiveTimeout = 2000
$udpClient.Connect($targetAddress, $Port)

function Send-DSXPacket([object[]]$Instructions) {
    $json = @{ instructions = $Instructions } | ConvertTo-Json -Depth 6 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    [void]$udpClient.Send($bytes, $bytes.Length)
    $sender = [System.Net.IPEndPoint]::new($targetAddress, 0)
    $reply = $udpClient.Receive([ref]$sender)
    [System.Text.Encoding]::UTF8.GetString($reply)
}

$effectAttempted = $false
try {
    Send-DSXPacket @(@{ type = 0; parameters = @() })
    if ($TestEffects) {
        $effectAttempted = $true
        # Numeric wire enums: TriggerUpdate=1, RGBUpdate=2, Right=2,
        # FEEDBACK=21. Start 1, force 2: brief, low-force resistance.
        Send-DSXPacket @(
            @{ type = 2; parameters = @($ControllerIndex, 0, 80, 80, 128) },
            @{ type = 1; parameters = @($ControllerIndex, 2, 21, 1, 2) }
        )
        Start-Sleep -Milliseconds 750
    }
}
finally {
    if ($effectAttempted) {
        try { Send-DSXPacket @(@{ type = 7; parameters = @($ControllerIndex) }) }
        catch { Write-Warning 'Reset could not be confirmed. Disable Game mod support in DS4Windows to release the effects.' }
    }
    $udpClient.Dispose()
}
