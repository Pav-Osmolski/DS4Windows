$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'DS4Windows.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Extract the complete ZIP before running this launcher.' }
if (Get-Process -Name DS4Windows -ErrorAction SilentlyContinue) { throw 'Fully exit the running DS4Windows first, then run this launcher again. No process was stopped.' }
Write-Host 'Issue #90 audio-route diagnostic - testing only'
Write-Host 'Fully exit your normal DS4Windows and other controller mappers first.'
Write-Host 'This records 30 seconds of game and microphone audio, your active profile, audio-device/session metadata and logs.'
Write-Host 'Files stay local. Review the ZIP before sharing. Nothing is uploaded automatically.'
Write-Host 'If you do not want audio recorded, close this launcher.'
if ((Read-Host 'Type I AGREE to open the preview with diagnostic capture available') -cne 'I AGREE') { exit }
$nonce = [Guid]::NewGuid().ToString('N')
$diag = Join-Path $root 'Diagnostics'
New-Item -ItemType Directory -Force -Path $diag | Out-Null
$env:DS4WINDOWS_ISSUE90_CAPTURE = '1'
$env:DS4WINDOWS_ISSUE90_NONCE = $nonce
$process = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru
Remove-Item Env:DS4WINDOWS_ISSUE90_CAPTURE
Remove-Item Env:DS4WINDOWS_ISSUE90_NONCE
Write-Host 'Import your Default.xml profile and connect ONLY the affected controller as Controller 1.'
Write-Host 'Keep Xbox 360 emulation, controller audio and microphone enabled, with your usual settings.'
Write-Host 'Set NVIDIA to the DS4Windows Controller 1 Microphone and separate audio tracks. Start an NVIDIA recording with game audio playing.'
Write-Host 'Keep the preview open until the diagnostic ZIP is finished.'
Write-Host ''
Write-Host 'During the capture:'
Write-Host '  First 10 seconds: game audio playing through the headset; stay quiet.'
Write-Host '  Next 10 seconds: set ONLY DS4Windows Headset / AUX volume to 0%; stay quiet. Keep game/NVIDIA/Windows volume unchanged.'
Write-Host '  Last 10 seconds: restore Headset / AUX volume and say a short sentence.'
if ((Read-Host 'When ready, type RECORD to capture audio') -cne 'RECORD') { exit }
Set-Content -LiteralPath (Join-Path $diag "$nonce-start.request") -Value $nonce -Encoding ascii
$recording = Join-Path $diag "$nonce-recording.txt"
$complete = Join-Path $diag "$nonce-complete.txt"
$errorFile = Join-Path $diag "$nonce-error.txt"
$deadline = [DateTime]::UtcNow.AddSeconds(80)
while (-not (Test-Path -LiteralPath $recording)) {
    if (Test-Path -LiteralPath $errorFile) { throw (Get-Content -LiteralPath $errorFile -Raw) }
    if ([DateTime]::UtcNow -gt $deadline) { throw 'The preview did not start capture. Make sure the normal DS4Windows was fully closed, then restart this launcher.' }
    Start-Sleep -Milliseconds 200
}
Write-Host 'CAPTURE STARTED: stay quiet, game audio playing.'
Start-Sleep -Seconds 10
Write-Host 'NOW: set DS4Windows Headset / AUX volume to 0%; stay quiet.'
Start-Sleep -Seconds 10
Write-Host 'NOW: restore Headset / AUX volume and say a short sentence.'
while (-not (Test-Path -LiteralPath $complete)) {
    if (Test-Path -LiteralPath $errorFile) { throw (Get-Content -LiteralPath $errorFile -Raw) }
    if ([DateTime]::UtcNow -gt $deadline) { throw 'Capture did not finish. Keep the app open and share the Diagnostics folder/error file for investigation.' }
    Start-Sleep -Milliseconds 250
}
$zip = (Get-Content -LiteralPath $complete -Raw).Trim()
Write-Host 'FINISHED. Stop the NVIDIA recording. Review and share this ZIP plus the original NVIDIA MP4:'
Write-Host $zip
Start-Process explorer.exe -ArgumentList @('/select,', ('"' + $zip + '"'))
Read-Host 'Press Enter to close this launcher'
