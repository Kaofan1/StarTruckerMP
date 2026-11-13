@echo off
setlocal

set "ROOT=%~dp0"
set "ISCC="

if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
    echo Inno Setup 6 was not found.
    echo Install it from https://jrsoftware.org/isdl.php and run this script again.
    echo Portable launcher build is still available through build-launcher-winx64.cmd.
    exit /b 1
)

call "%ROOT%build-launcher-winx64.cmd" %~1
if errorlevel 1 exit /b 1

rem Same build number as the launcher, so the installed app reports 1.0.<N>.
set "ISCC_VERSION_ARGS="
if not "%~1"=="" set "ISCC_VERSION_ARGS=/DMyAppVersion=1.0.%~1"

echo.
echo Building the installer with "%ISCC%"...
"%ISCC%" %ISCC_VERSION_ARGS% "%ROOT%installer\StarTruckerMP.iss"

if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Installer build failed with exit code %ERRORLEVEL%.
    exit /b 1
)

echo.
echo Installer created: %ROOT%artifacts\installer\StarTruckerMP-Setup.exe
exit /b 0
