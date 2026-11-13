# Development

[English](README.md) | **Polski**

StarTruckerMP to solution .NET 10. Launcher jest aplikacją desktopową Avalonia, klient to plugin
BepInEx 6 IL2CPP, serwer to ASP.NET Core, a overlay to przeglądarka CEF w osobnym procesie.

## Układ projektów

| Projekt | Target | Rola |
| --- | --- | --- |
| `StarTruckerMP.Client` | `net10.0`, win-x64 | plugin BepInEx: synchronizacja, czat głosowy, most do overlaya |
| `StarTruckerMP.Server` | `net10.0` | serwer dedykowany, HTTPS API, UI overlaya SvelteKit w `overlay-ui/` |
| `StarTruckerMP.Shared` | `net10.0` | pakiety, DTO i stałe protokołu wspólne dla klienta i serwera |
| `StarTruckerMP.Launcher` | `net10.0-windows` | launcher Avalonia i instalator moda |
| `StarTruckerMP.Overlay.Host` | win-x64 | osobny proces rysujący overlay w grze |
| `StarTruckerMP.Overlay.Browser` | win-x64 | wrapper CEF używany przez host overlaya |
| `StarTruckerMP.Overlay.Core` | `net10.0` | komendy i modele IPC overlaya |
| `StarTruckerMP.Plugin.OldImplementation` | `net10.0` | zarchiwizowany prototyp, trzymany jako referencja |

## Wymagania

- .NET 10 SDK
- Node.js 20 lub nowszy dla `StarTruckerMP.Server/overlay-ui`
- [Inno Setup 6](https://jrsoftware.org/isdl.php) do instalatora Windows
- DLL-e z gry (`Assembly-CSharp.dll`, `UnityEngine.*`, core BepInEx) do kompilacji klienta - patrz
  [docs/ci/self-hosted-runner.md](../ci/self-hosted-runner.md)

## Budowanie

```cmd
:: całe solution (klient wymaga DLL-ek z gry)
dotnet build StarTruckerMP.sln -c Release

:: sam launcher, portable
build-launcher-winx64.cmd

:: launcher i instalator
build-installer-winx64.cmd
```

### Paczka serwera lokalnego

Asset `StarTruckerMP-server-win-x64.zip` z wydania to plik, który launcher rozpakowuje do
`%APPDATA%\StarTruckerMP\server`, a publikuje go workflow CI. Niczego nie trzeba budować ręcznie:
launcher porównuje tag zapisany w tym folderze (`package-tag.txt`) z najnowszym wydaniem i podmienia
pliki, gdy pojawi się nowsza paczka. `server.json`, certyfikat i logi zostają nietknięte, a działający
serwer jest pomijany i aktualizowany przy następnym starcie, bo Windows blokuje jego plik exe.

Do lokalnego sprawdzenia uruchom tę samą publikację co workflow. Korzysta ona z gotowego
`StarTruckerMP.Server\wwwroot`, więc najpierw zbuduj UI overlaya; bez tego pliku serwer wystartuje z
pustą trasą overlaya.

```bash
# UI overlaya (raz przed lokalną publikacją serwera)
cd StarTruckerMP.Server/overlay-ui
npm ci
npm run build

# serwer bez przebudowy UI Svelte
dotnet publish StarTruckerMP.Server/StarTruckerMP.Server.csproj -c Release -r win-x64 \
  --no-self-contained -p:PublishSingleFile=true -p:RunSvelteBuild=false -o ./artifacts/server-win
```

## Konwencje

- Nazwa produktu to wszędzie **StarTruckerMP**: foldery, namespace'y, assembly, pliki, assety wydań
  i nazwy plików konfiguracyjnych. Nie wracamy do starej pisowni `StarTruckMP`.
- Nie używamy myślnika em dash w kodzie, komentarzach, dokumentacji ani tekstach UI. Zamiast niego
  jest zwykły myślnik.
- Teksty UI należą do `StarTruckerMP.Launcher/Localization/Strings.cs` i muszą istnieć w angielskim
  i polskim.
- Ustawienia i logi launchera leżą w `%APPDATA%\StarTruckerMP`, zainstalowany launcher w
  `%LOCALAPPDATA%\StarTruckerMP`.

## CI

`.github/workflows/main.yml` ma dwa joby:

| Job | Runner | Wynik |
| --- | --- | --- |
| `launcher` | `windows-latest` | zip launchera i `StarTruckerMP-Setup.exe` |
| `build` | `self-hosted, docker, dotnet10` | klient, overlay i buildy serwera |

Job `build` uruchamia się tylko wtedy, gdy zmienna repozytorium `ENABLE_SELF_HOSTED_BUILD` ma wartość
`true`, a runner z tymi etykietami jest online. Inaczej jest pomijany, a release zawiera wyłącznie
launcher i instalator.

Wydanie zawiera do sześciu plików:

- `StarTruckerMP-Setup.exe`
- `StarTruckerMP-Launcher-win-x64.zip`
- `StarTruckerMP-bepinex-plugin-win-x64.zip`
- `StarTruckerMP-server-win-x64.zip`
- `StarTruckerMP-server-linux-x64.zip`
- `StarTruckerMP-server-docker.zip`

Wersja launchera jest stemplowana jako `1.0.<run_number>` i odpowiada tagowi `alpha-v<N>`, z którym
porównuje się checker aktualizacji.

## Dokumentacja

| Temat | English | Polski |
| --- | --- | --- |
| Launcher | [launcher/README.en.md](../launcher/README.en.md) | [launcher/README.md](../launcher/README.md) |
| Hosting serwera | [server-hosting/README.md](../server-hosting/README.md) | [server-hosting/README.pl.md](../server-hosting/README.pl.md) |
