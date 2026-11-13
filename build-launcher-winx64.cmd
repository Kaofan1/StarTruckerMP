@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%artifacts\launcher"

rem Optional build number, so the release tag alpha-v<N> matches the launcher version
rem 1.0.<N> the update checker compares against. Without it the build reports 1.0.0.
set "VERSION_ARGS="
if not "%~1"=="" set "VERSION_ARGS=-p:Version=1.0.%~1"

rem The dotnet host alone cannot publish, and a machine without the SDK on PATH fails
rem without a non-zero exit code, so prefer the local SDK when it exists.
set "DOTNET=dotnet"
if exist "%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe" (
    set "DOTNET=%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe"
    set "DOTNET_ROOT=%LOCALAPPDATA%\dotnet-sdk-10"
)

echo Publishing StarTruckerMP launcher (win-x64, self-contained)...
if exist "%OUT%" rd /s /q "%OUT%"

"%DOTNET%" publish "%ROOT%StarTruckerMP.Launcher\StarTruckerMP.Launcher.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    %VERSION_ARGS% ^
    -o "%OUT%"

if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Launcher publish failed with exit code %ERRORLEVEL%.
    exit /b 1
)

if not exist "%OUT%\StarTruckerMP.Launcher.exe" (
    echo.
    echo Publish produced no launcher executable in %OUT%.
    exit /b 1
)

echo.
echo Launcher published to: %OUT%
if defined VERSION_ARGS echo Version: 1.0.%~1
exit /b 0
