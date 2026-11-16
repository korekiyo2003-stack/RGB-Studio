@echo off
setlocal
echo ==============================================
echo   Building RGB Studio
echo ==============================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
if not exist %CSC% (
    echo [ERROR] csc.exe not found in Microsoft.NET Framework directories!
    exit /b 1
)

echo Using compiler: %CSC%
if not exist "..\release" mkdir "..\release"

%CSC% /target:winexe /optimize+ /out:"..\release\RGB Studio.exe" /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Management.dll ..\src\BoardRgb.cs ..\src\LightingPanel.cs ..\src\MemoryLighting.cs ..\src\MemoryShow.cs ..\src\MotherboardDetector.cs ..\src\RgbStudio.cs ..\src\RigPreview.cs ..\src\SceneCard.cs

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] Compiled successfully to release\RGB Studio.exe
    copy /y "..\presets\scenes.json" "..\release\scenes.json" >nul
    if not exist "..\release\preferences.json" copy /y "..\presets\preferences.example.json" "..\release\preferences.json" >nul
) else (
    echo [FAILED] Compilation failed with code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

endlocal
