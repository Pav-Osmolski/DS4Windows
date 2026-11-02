@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Issue98-Diagnostics.ps1"
if errorlevel 1 pause
