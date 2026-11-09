@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%artifacts\launcher"

echo Publishing StarTruckerMP launcher (win-x64, self-contained)...
dotnet publish "%ROOT%StarTruckMP.Launcher\StarTruckMP.Launcher.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "%OUT%"

if errorlevel 1 (
    echo.
    echo Launcher publish failed.
    exit /b 1
)

echo.
echo Launcher published to: %OUT%
exit /b 0
