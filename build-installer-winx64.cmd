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

call "%ROOT%build-launcher-winx64.cmd"
if errorlevel 1 exit /b 1

echo.
echo Building the installer with "%ISCC%"...
"%ISCC%" "%ROOT%installer\StarTruckerMP.iss"

if errorlevel 1 (
    echo.
    echo Installer build failed.
    exit /b 1
)

echo.
echo Installer created: %ROOT%artifacts\installer\StarTruckerMP-Setup.exe
exit /b 0
