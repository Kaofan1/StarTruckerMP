@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%artifacts\server-win"
set "ISCC="

if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
    echo Inno Setup 6 was not found.
    echo Install it from https://jrsoftware.org/isdl.php and run this script again.
    exit /b 1
)

set "DOTNET=dotnet"
if exist "%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe" (
    set "DOTNET=%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe"
    set "DOTNET_ROOT=%LOCALAPPDATA%\dotnet-sdk-10"
)

set "VERSION_ARGS="
if not "%~1"=="" set "VERSION_ARGS=-p:Version=1.0.%~1"

echo Publishing StarTruckerMP server (win-x64, self-contained)...
if exist "%OUT%" rd /s /q "%OUT%"

"%DOTNET%" publish "%ROOT%StarTruckerMP.Server\StarTruckerMP.Server.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:RunSvelteBuild=false ^
    -p:EnableWindowsTargeting=true ^
    %VERSION_ARGS% ^
    -o "%OUT%"

if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Server publish failed with exit code %ERRORLEVEL%.
    exit /b 1
)

rem An operator's settings never travel inside the installer.
if exist "%OUT%\server.json" del /q "%OUT%\server.json"
if exist "%OUT%\certs" rd /s /q "%OUT%\certs"

echo.
echo Building the server installer with "%ISCC%"...
set "ISCC_VERSION_ARGS="
if not "%~1"=="" set "ISCC_VERSION_ARGS=/DMyAppVersion=1.0.%~1"
"%ISCC%" %ISCC_VERSION_ARGS% /DSourceDir="..\artifacts\server-win" "%ROOT%installer\StarTruckerMP-Server.iss"

if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Installer build failed with exit code %ERRORLEVEL%.
    exit /b 1
)

echo.
echo Installer created: %ROOT%artifacts\server-installer\StarTruckerMP-Server-Setup.exe
exit /b 0
