# Development

**English** | [Polski](README.pl.md)

StarTruckerMP is a .NET 10 solution. The launcher is an Avalonia desktop app, the client is a BepInEx 6
IL2CPP plugin, the server is an ASP.NET Core app, and the overlay is a CEF browser hosted in its own
process.

## Project layout

| Project | Target | Purpose |
| --- | --- | --- |
| `StarTruckerMP.Client` | `net10.0`, win-x64 | BepInEx plugin: sync, voice chat, overlay bridge |
| `StarTruckerMP.Server` | `net10.0` | dedicated server, HTTPS API, SvelteKit overlay UI in `overlay-ui/` |
| `StarTruckerMP.Shared` | `net10.0` | packets, DTOs and protocol constants shared by client and server |
| `StarTruckerMP.Launcher` | `net10.0-windows` | Avalonia launcher and mod installer |
| `StarTruckerMP.Overlay.Host` | win-x64 | separate process that draws the in-game overlay |
| `StarTruckerMP.Overlay.Browser` | win-x64 | CEF wrapper used by the overlay host |
| `StarTruckerMP.Overlay.Core` | `net10.0` | overlay IPC commands and models |
| `StarTruckerMP.Plugin.OldImplementation` | `net10.0` | archived prototype, kept for reference |

## Requirements

- .NET 10 SDK
- Node.js 20 or newer for `StarTruckerMP.Server/overlay-ui`
- [Inno Setup 6](https://jrsoftware.org/isdl.php) for the Windows installer
- Game DLLs (`Assembly-CSharp.dll`, `UnityEngine.*`, BepInEx core) to compile the client - see
  [docs/ci/self-hosted-runner.md](../ci/self-hosted-runner.md) (Polish)

## Building

```cmd
:: whole solution (client needs the game DLLs)
dotnet build StarTruckerMP.sln -c Release

:: launcher only, portable
build-launcher-winx64.cmd

:: launcher and installer
build-installer-winx64.cmd

:: server package and release zip
build-server-winx64.cmd
```

### Server package

`build-server-winx64.cmd` publishes a self-contained single-file `win-x64` build into
`artifacts\server-win` and packs it as `StarTruckerMP-server-win-x64.zip` in the repository root. That
zip is the release asset the launcher downloads and unpacks into `%APPDATA%\StarTruckerMP\server`, so
the **LOCAL SERVER** card in the launcher runs exactly those bits.

The script reuses `StarTruckerMP.Server\wwwroot`, so build the overlay UI first. It only warns when
`wwwroot\overlay\index.html` is missing, and without that file the server starts with an empty overlay
route. On a machine where `dotnet` is not on `PATH`, the script picks the SDK installed in
`%LOCALAPPDATA%\dotnet-sdk-10`.

The zip is written by `tar.exe` from Windows, because `Compress-Archive` and .NET Framework `ZipFile`
store entry names with backslashes. Forward slashes keep the archive portable and let the launcher
extract it with `ZipFile.ExtractToDirectory`.

```bash
# overlay UI (needed once before build-server-winx64.cmd)
cd StarTruckerMP.Server/overlay-ui
npm ci
npm run build

# server without rebuilding the Svelte UI
dotnet publish StarTruckerMP.Server/StarTruckerMP.Server.csproj -c Release -r win-x64 \
  --no-self-contained -p:PublishSingleFile=true -p:RunSvelteBuild=false -o ./artifacts/server-win
```

## Conventions

- The product name is **StarTruckerMP** everywhere: folders, namespaces, assemblies, files, release
  assets and configuration file names. Do not reintroduce the older `StarTruckMP` spelling.
- Do not use the em dash in code, comments, documentation or UI strings. Use a plain hyphen instead.
- UI text belongs in `StarTruckerMP.Launcher/Localization/Strings.cs` and must exist in both English
  and Polish.
- Launcher settings and logs live in `%APPDATA%\StarTruckerMP`, the installed launcher in
  `%LOCALAPPDATA%\StarTruckerMP`.

## Continuous integration

`.github/workflows/main.yml` has two jobs:

| Job | Runner | Output |
| --- | --- | --- |
| `launcher` | `windows-latest` | launcher zip and `StarTruckerMP-Setup.exe` |
| `build` | `self-hosted, docker, dotnet10` | client, overlay and server builds |

The `build` job only runs when the repository variable `ENABLE_SELF_HOSTED_BUILD` is `true` and a runner
with those labels is online. Otherwise it is skipped and the release contains only the launcher and the
installer.

A release contains up to six files:

- `StarTruckerMP-Setup.exe`
- `StarTruckerMP-Launcher-win-x64.zip`
- `StarTruckerMP-bepinex-plugin-win-x64.zip`
- `StarTruckerMP-server-win-x64.zip`
- `StarTruckerMP-server-linux-x64.zip`
- `StarTruckerMP-server-docker.zip`

The launcher version is stamped as `1.0.<run_number>`, which matches the `alpha-v<N>` release tag the
update checker compares against.

## Documentation

| Topic | English | Polski |
| --- | --- | --- |
| Launcher | [launcher/README.en.md](../launcher/README.en.md) | [launcher/README.md](../launcher/README.md) |
| Server hosting | [server-hosting/README.md](../server-hosting/README.md) | [server-hosting/README.pl.md](../server-hosting/README.pl.md) |
