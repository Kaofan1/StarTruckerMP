# StarTruckerMP

![Star Trucker Multiplayer launcher](docs/images/banner.png)

Please report bugs & issues to the [Issues](https://github.com/Kaofan1/StarTruckerMP/issues) tab.

> [!CAUTION]
> This is an early alpha build, expect bugs and crashes.
> This is **NOT** intended for production at this point.

## 🖥️ Launcher (recommended)

`StarTruckerMP-Setup.exe` installs the custom launcher, which detects the Steam copy of Star Trucker,
downloads BepInEx + the client, writes the server connection for you and starts the game through Steam
so Steam authentication keeps working.

![Star Trucker Multiplayer launcher window](docs/images/launcher.png)

1. Download `StarTruckerMP-Setup.exe` from [Releases](https://github.com/Kaofan1/StarTruckerMP/releases)
2. Run it - it installs per user, no administrator rights required
3. Open **Star Trucker Multiplayer** from the Start menu
4. Pick a server, press **Install / repair mod**, then **PLAY**

The portable build `StarTruckerMP-Launcher-win-x64.zip` needs no installation at all.
Build steps, file locations and troubleshooting: [docs/launcher](docs/launcher/README.md) (Polish).

## 🎮 Gamers
For the manual installation instructions check out the [Wiki](https://github.com/Kaofan1/StarTruckerMP/wiki/%F0%9F%8E%AE-Installation)

> [!NOTE]
> When the plugin is loaded it will try to authenticate and connect to the server.
> The launcher writes the server address into the BepInEx config for you; you can also edit
> `BepInEx/config/StarTruckerMP.Client.cfg` by hand.

https://github.com/user-attachments/assets/3bb23b56-a32f-426d-af84-c9c058741ef4

## 🌐 Server Hosting

> [!CAUTION]
> This is just a quick and dirty implementation to get the server up and running, expect bugs and crashes. This is **NOT** intended for production at this point.

1. Download latest build of the Server
2. Install [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
2. Run the `StarTruckerMP.Server.exe`!

> [!NOTE]
> Check the [server hosting wiki](https://github.com/Kaofan1/StarTruckerMP/wiki/%F0%9F%8C%90-Server-hosting)

## 🚧 Developers

Anyone willing to contribute feel free to jump in!

> [!NOTE]
> Check out [the development wiki](https://github.com/Kaofan1/StarTruckerMP/wiki/%F0%9F%9A%A7-Development)

```cmd
:: launcher (portable) -> artifacts\launcher
build-launcher-winx64.cmd

:: launcher + Windows installer -> artifacts\installer\StarTruckerMP-Setup.exe
build-installer-winx64.cmd
```

> [!NOTE]
> The launcher and installer are built on a GitHub-hosted Windows runner. Building the client and
> server additionally requires a self-hosted runner with the game interop DLLs - see
> [docs/ci/self-hosted-runner.md](docs/ci/self-hosted-runner.md) (Polish).

# Credits

- [JayJay34](https://github.com/JayJay34) - who started the project
- [BepInEx](https://github.com/bepinex/bepinex) - the framework that makes everything possible
- [rnnoise](https://github.com/xiph/rnnoise) - library to reduce noise on PTT
- [RNNoise.Net](https://github.com/Yellow-Dog-Man/RNNoise.Net) - api for .NET to run rnnoise
- [NWaves](https://github.com/ar1st0crat/NWaves) - library used to make DSP in .NET
- [Concentus](https://github.com/lostromb/concentus) - api for .NET to run Opus encoder/decoder
