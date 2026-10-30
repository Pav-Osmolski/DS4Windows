@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Issue90-Audio-Diagnostic.ps1"
if errorlevel 1 pause
