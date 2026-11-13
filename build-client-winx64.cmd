@echo off
setlocal

rem The dotnet host alone cannot publish and can exit with 0 after a failure, so prefer
rem the local SDK when it exists and check every publish result.
set "DOTNET=dotnet"
if exist "%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe" (
    set "DOTNET=%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe"
    set "DOTNET_ROOT=%LOCALAPPDATA%\dotnet-sdk-10"
)

"%DOTNET%" publish StarTruckerMP.Client/StarTruckerMP.Client.csproj ^
    -c Debug ^
    -r win-x64 ^
    -p:EnableWindowsTargeting=true ^
    -o "%StarTruckBepInEx%/plugins/StarTruckerMP"

if not "%ERRORLEVEL%"=="0" (
    echo Client publish to StarTruckBepInEx failed with exit code %ERRORLEVEL%.
    exit /b 1
)

"%DOTNET%" publish StarTruckerMP.Client/StarTruckerMP.Client.csproj ^
    -c Debug ^
    -r win-x64 ^
    -p:EnableWindowsTargeting=true ^
    -o "%StarTruckBepInExSteam%/plugins/StarTruckerMP"

if not "%ERRORLEVEL%"=="0" (
    echo Client publish to StarTruckBepInExSteam failed with exit code %ERRORLEVEL%.
    exit /b 1
)
