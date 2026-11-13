#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the StarTruckerMP dedicated server as a Windows service.

.DESCRIPTION
    Downloads the server package from a GitHub release, unpacks it, writes server.json when it is
    missing and registers the executable as a Windows service, so it starts with the machine.
    An existing server.json and the certs folder are kept during an upgrade.

.PARAMETER Version
    Release tag to install, for example alpha-v67. The default is the newest release.

.PARAMETER Port
    Game and HTTPS port, used only when server.json is created.

.PARAMETER MaxPlayers
    Player limit, used only when server.json is created.

.PARAMETER InstallDir
    Target folder. Defaults to Program Files.

.PARAMETER ServiceName
    Windows service name.

.PARAMETER AdminPassword
    Admin password for the HTTP API. A random one is generated when omitted.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-server-windows.ps1 -Port 7777
#>
[CmdletBinding()]
param(
    [string]$Version = "latest",
    [int]$Port = 7777,
    [int]$MaxPlayers = 100,
    [string]$InstallDir = "$env:ProgramFiles\StarTruckerMP Server",
    [string]$ServiceName = "StarTruckerMPServer",
    [string]$AdminPassword
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = "Kaofan1/StarTruckerMP"
$asset = "StarTruckerMP-server-win-x64.zip"
$displayName = "StarTruckerMP Dedicated Server"

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell window (Run as administrator)."
    }
}

function New-RandomPassword {
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    -join (1..24 | ForEach-Object { $alphabet[(Get-Random -Maximum $alphabet.Length)] })
}

function Get-Release {
    $headers = @{ "User-Agent" = "StarTruckerMP-installer" }

    if ($Version -eq "latest") {
        return Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers $headers
    }

    return Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$Version" -Headers $headers
}

Assert-Administrator

if ($Port -lt 1 -or $Port -gt 65535) { throw "Port must be between 1 and 65535." }
if ($MaxPlayers -lt 1 -or $MaxPlayers -gt 256) { throw "MaxPlayers must be between 1 and 256." }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Step "Resolving release $Version"
$release = Get-Release
$download = $release.assets | Where-Object { $_.name -eq $asset } | Select-Object -First 1

if (-not $download) {
    throw "Release $($release.tag_name) has no $asset. Use -Version with a release that includes the server package."
}

Write-Host "Release $($release.tag_name), package $([math]::Round($download.size / 1MB, 1)) MB"

$temp = Join-Path $env:TEMP ("StarTruckerMP-" + [Guid]::NewGuid().ToString("N"))
$zip = Join-Path $temp $asset
$staging = Join-Path $temp "server"

New-Item -ItemType Directory -Path $temp -Force | Out-Null

try {
    Write-Step "Downloading $asset"
    Invoke-WebRequest -Uri $download.browser_download_url -OutFile $zip -UseBasicParsing

    Write-Step "Unpacking to $InstallDir"
    Expand-Archive -Path $zip -DestinationPath $staging -Force
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

    # Keep the operator's settings and certificate during an upgrade.
    if (Test-Path (Join-Path $InstallDir "server.json")) {
        Remove-Item (Join-Path $staging "server.json") -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path (Join-Path $InstallDir "certs")) {
        Remove-Item (Join-Path $staging "certs") -Recurse -Force -ErrorAction SilentlyContinue
    }

    Copy-Item (Join-Path $staging "*") -Destination $InstallDir -Recurse -Force

    $exe = Join-Path $InstallDir "StarTruckerMP.Server.exe"
    if (-not (Test-Path $exe)) {
        throw "The package does not contain StarTruckerMP.Server.exe."
    }

    Write-Step "Writing server.json"
    $settingsPath = Join-Path $InstallDir "server.json"
    $generatedPassword = $false

    if (-not (Test-Path $settingsPath)) {
        if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
            $AdminPassword = New-RandomPassword
            $generatedPassword = $true
        }

        $settings = [ordered]@{
            IpAddress           = "0.0.0.0"
            Port                = $Port
            MaxPlayers          = $MaxPlayers
            ApiAdminUsername    = "admin"
            ApiAdminPassword    = $AdminPassword
            ApiTokenLifetime    = "08:00:00"
            XboxRequiredTitleId = [Convert]::ToUInt32("6E2BD7CD", 16)
            SteamWebApiKey      = ""
            SteamAppId          = 2380050
        }

        $json = $settings | ConvertTo-Json -Depth 4
        [IO.File]::WriteAllText($settingsPath, $json, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Created $settingsPath"
    }
    else {
        Write-Host "Keeping the existing $settingsPath"
        $existing = Get-Content $settingsPath -Raw | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains "ApiAdminPassword") {
            $AdminPassword = $existing.ApiAdminPassword
        }
    }

    Write-Step "Registering service $ServiceName"
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Host "Service exists, stopping and replacing it"
        if ($service.Status -ne "Stopped") {
            sc.exe stop $ServiceName | Out-Null
            for ($i = 0; $i -lt 30 -and (Get-Service $ServiceName).Status -ne "Stopped"; $i++) {
                Start-Sleep -Seconds 1
            }
        }

        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    sc.exe create $ServiceName binPath= "`"$exe`"" start= auto DisplayName= "$displayName" | Out-Null
    sc.exe description $ServiceName "StarTruckerMP dedicated server. Settings: $settingsPath" | Out-Null

    foreach ($ruleName in @("StarTruckerMP Dedicated Server (TCP $Port)", "StarTruckerMP Dedicated Server (UDP $Port)")) {
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            $protocol = if ($ruleName -match "TCP") { "TCP" } else { "UDP" }
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol $protocol -LocalPort $Port | Out-Null
            Write-Host "Added firewall rule $ruleName"
        }
    }

    Write-Step "Starting the service"
    sc.exe start $ServiceName | Out-Null

    $running = $false
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 1
        if ((Get-Service $ServiceName).Status -eq "Running") {
            $running = $true
            break
        }
    }

    Write-Host ""
    if ($running) {
        Write-Host "StarTruckerMP server $($release.tag_name) is running." -ForegroundColor Green
    }
    else {
        Write-Warning "The service did not report Running within 45 seconds. Check the Windows event log and $settingsPath."
    }

    Write-Host "Install folder : $InstallDir"
    Write-Host "Settings      : $settingsPath"
    Write-Host "Status page   : https://localhost:$Port/"
    Write-Host "Players join  : <public ip>:$Port (UDP and TCP)"
    Write-Host "Service       : sc.exe query $ServiceName"

    if ($generatedPassword) {
        Write-Host ""
        Write-Host "API admin     : admin / $AdminPassword" -ForegroundColor Yellow
        Write-Host "Save that password, it is written in server.json and not shown again."
    }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
