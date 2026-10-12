@echo off
setlocal
echo ===================================================
echo   Installing LegionPulse System Monitor Overlay
echo ===================================================

call "%~dp0build.bat"
if not exist "%~dp0LegionMonitorOverlay.exe" (
    echo [ERROR] Build failed. Aborting install.
    pause
    exit /b 1
)

set TARGET_DIR=%LOCALAPPDATA%\LegionMonitor
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo Copying binary to %TARGET_DIR% ...
copy /Y "%~dp0LegionMonitorOverlay.exe" "%TARGET_DIR%\LegionMonitorOverlay.exe"

echo Creating desktop shortcut...
powershell -NoProfile -Command "$wsh = New-Object -ComObject WScript.Shell; $s = $wsh.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\Legion Monitor Overlay.lnk'); $s.TargetPath = '%TARGET_DIR%\LegionMonitorOverlay.exe'; $s.WorkingDirectory = '%TARGET_DIR%'; $s.Save()"

echo Registering run-on-startup in Windows registry...
powershell -NoProfile -Command "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LegionMonitorOverlay' -Value '\"%TARGET_DIR%\LegionMonitorOverlay.exe\"'"

echo Launching overlay...
start "" "%TARGET_DIR%\LegionMonitorOverlay.exe"

echo [SUCCESS] LegionPulse installed and active!
pause
endlocal
