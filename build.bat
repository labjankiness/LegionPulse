@echo off
setlocal
echo ===================================================
echo   Compiling LegionPulse System Monitor Overlay
echo ===================================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set LIB_WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF
set SRC=%~dp0src\LegionMonitor.cs
set OUT=%~dp0LegionMonitorOverlay.exe

if not exist "%CSC%" (
    echo [ERROR] .NET Framework C# compiler not found at %CSC%
    pause
    exit /b 1
)

echo Compiling %SRC% ...
"%CSC%" /target:winexe /optimize+ /lib:"%LIB_WPF%" /r:System.dll,System.Xaml.dll,PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll /out:"%OUT%" "%SRC%"

if exist "%OUT%" (
    echo [SUCCESS] Successfully compiled: %OUT%
) else (
    echo [ERROR] Compilation failed.
    pause
    exit /b 1
)

endlocal
