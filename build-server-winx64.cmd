@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%artifacts\server-win"
set "ZIP=%ROOT%StarTruckerMP-server-win-x64.zip"

rem Prefer a local SDK when dotnet is not on PATH (the installed host alone cannot publish).
set "DOTNET=dotnet"
if exist "%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe" (
    set "DOTNET=%LOCALAPPDATA%\dotnet-sdk-10\dotnet.exe"
    set "DOTNET_ROOT=%LOCALAPPDATA%\dotnet-sdk-10"
)

echo Publishing StarTruckerMP server (win-x64, self-contained)...
if exist "%OUT%" rd /s /q "%OUT%"
"%DOTNET%" publish "%ROOT%StarTruckerMP.Server\StarTruckerMP.Server.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:RunSvelteBuild=false ^
    -p:EnableWindowsTargeting=true ^
    -o "%OUT%"

if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Server publish failed with exit code %ERRORLEVEL%.
    exit /b 1
)

if not exist "%OUT%\StarTruckerMP.Server.exe" (
    echo.
    echo Publish output is missing StarTruckerMP.Server.exe.
    exit /b 1
)

rem The overlay UI is built by the CI workflow, so this script reuses whatever sits in
rem StarTruckerMP.Server\wwwroot. Without it the server starts, but /overlay stays empty.
if not exist "%ROOT%StarTruckerMP.Server\wwwroot\overlay\index.html" (
    echo.
    echo WARNING: StarTruckerMP.Server\wwwroot\overlay\index.html is missing.
    echo          Build it once with: cd StarTruckerMP.Server\overlay-ui ^&^& npm ci ^&^& npm run build
)

echo.
echo Packing the release zip: %ZIP%
if exist "%ZIP%" del /f /q "%ZIP%"

rem Windows bsdtar writes forward-slash entry names, so the launcher's extractor and the
rem overlay files stay portable. PowerShell's Compress-Archive writes backslashes instead.
"%SystemRoot%\System32\tar.exe" -a -c -f "%ZIP%" -C "%OUT%" .
if not "%ERRORLEVEL%"=="0" (
    echo.
    echo Packing the release zip failed with exit code %ERRORLEVEL%.
    exit /b 1
)

for %%F in ("%ZIP%") do echo Zip size: %%~zF bytes
echo.
echo Server published to: %OUT%
echo Release zip: %ZIP%
exit /b 0
