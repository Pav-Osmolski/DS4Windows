param(
    [ValidateSet('x86','x64')][string]$Architecture = 'x64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Install Visual Studio C++ Build Tools and a Windows SDK first.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
$source = Join-Path $PSScriptRoot 'probe.cpp'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$exe = Join-Path $destination "GenericJoystickProbe-$Architecture.exe"
$object = Join-Path $destination "probe-$Architecture.obj"
# Only compile a read-only consumer. No driver/service or device mutations.
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $env:ComSpec
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.Arguments = "/d /s /c `"call `"$vcvars`" $Architecture >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /DUNICODE /D_UNICODE `"$source`" /Fo`"$object`" /Fe`"$exe`" /link dinput8.lib dxguid.lib winmm.lib user32.lib`""
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Probe compilation failed ($($process.ExitCode))." }
$process.Dispose()
Write-Output $exe
