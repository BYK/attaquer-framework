@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Leave this window open and report the error above.
  pause
  exit /b 1
)
echo Attaquer Taskbar installed and launched.
