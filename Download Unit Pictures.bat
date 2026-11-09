@echo off
rem Downloads Army Forge's unit pictures into UnitImages (see tools\download-unit-images.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\download-unit-images.ps1"
echo.
pause
