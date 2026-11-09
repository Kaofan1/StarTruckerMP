# Star Trucker Multiplayer — launcher i instalator

Launcher jest teraz głównym sposobem instalacji moda. Zamiast ręcznego kopiowania BepInEx i pluginu,
gracz uruchamia jeden plik `StarTruckerMP-Setup.exe`, a launcher robi resztę:

1. wykrywa zainstalowaną przez Steam grę **Star Trucker** (`appid 2380050`) — rejestr, `libraryfolders.vdf`, `appmanifest_2380050.acf`, dodatkowe biblioteki Steam,
2. pobiera i rozpakowuje **BepInEx 6 (IL2CPP, win-x64)** do folderu gry,
3. pobiera i rozpakowuje **klienta StarTruckMP** do `BepInEx/plugins/StarTruckMP`,
4. zapisuje wybrany serwer do `BepInEx/config/StarTruckMP.Client.cfg`,
5. uruchamia grę **przez Steam** (`steam://rungameid/2380050`), dzięki czemu logowanie SteamWorks działa normalnie.

## Instalacja dla gracza

1. Pobierz `StarTruckerMP-Setup.exe` z zakładki [Releases](https://github.com/Kaofan1/StarTruckerMP/releases).
2. Uruchom instalator. Instaluje się **per-user** (`%LOCALAPPDATA%\StarTruckerMP`), więc nie wymaga uprawnień administratora.
3. Uruchom **Star Trucker Multiplayer** z menu Start (skrót na pulpicie jest opcjonalny).
4. Sprawdź, czy launcher poprawnie wykrył grę. Jeśli nie — kliknij **Choose folder…** i wskaż katalog z `Star Trucker.exe`.
5. Dodaj serwer (**Add**), wpisz nazwę, host i port, zapisz (**Save server**).
6. Kliknij **Install / repair mod**, poczekaj na komunikat „Multiplayer mod is ready.”.
7. Kliknij **PLAY**. Gra wystartuje przez Steam, a po wejściu do sektora pojawi się overlay i połączenie z serwerem.

Nazwa pliku instalatora nie zawiera numeru wersji — zawsze jest to `StarTruckerMP-Setup.exe`.

## Co launcher zapisuje i gdzie

| Plik | Znaczenie |
| --- | --- |
| `%APPDATA%\StarTruckMP\launcher.json` | lista serwerów, ścieżka do gry, ustawienia startu |
| `%APPDATA%\StarTruckMP\launcher.log` | log launchera (przydatny przy zgłaszaniu błędów) |
| `%APPDATA%\StarTruckMP\launcher-crash.log` | zapis wyjątku, jeśli launcher nie wystartuje |
| `<gra>\BepInEx\config\StarTruckMP.Client.cfg` | adres/port serwera, mikrofon, `IgnoreSslValidation` |

Launcher nie nadpisuje pozostałych ustawień w pliku `.cfg` — zmienia tylko klucze, którymi zarządza.

## Wymagania

- Windows 10/11 x64
- Star Trucker w wersji **Steam** (wersja Xbox / Game Pass nie jest wspierana przez BepInEx IL2CPP)
- Steam uruchomiony i zalogowany (do weryfikacji biletu Steam na serwerze)

Wersja portable: rozpakuj `StarTruckerMP-Launcher-win-x64.zip` w dowolne miejsce i uruchom
`StarTruckerMP.Launcher.exe`. Build jest self-contained, nie wymaga instalacji .NET.

## Budowanie ze źródeł

Wymagania: .NET 10 SDK, opcjonalnie [Inno Setup 6](https://jrsoftware.org/isdl.php) do instalatora.

```cmd
:: sam launcher (portable) -> artifacts\launcher
build-launcher-winx64.cmd

:: launcher + instalator -> artifacts\installer\StarTruckerMP-Setup.exe
build-installer-winx64.cmd
```

Ręczne wywołanie kompilatora instalatora:

```cmd
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\StarTruckerMP.iss
```

Wersja w metadanych instalatora jest przekazywana przez `#define` (domyślnie `1.0.0`):

```cmd
ISCC.exe /DMyAppVersion=1.2.0 installer\StarTruckerMP.iss
```

## Rozwiązywanie problemów

| Objaw | Co zrobić |
| --- | --- |
| „Star Trucker was not found” | Kliknij **Choose folder…** i wskaż katalog z `Star Trucker.exe` (np. `…\steamapps\common\Star Trucker`). |
| Instalacja moda kończy się błędem pobierania | Sprawdź dostęp do `api.github.com` i `github.com` (proxy/firewall). GitHub API ma limit 60 zapytań na godzinę na adres IP. |
| Gra startuje, ale nie ma połączenia | Sprawdź port i adres serwera; przy własnym serwerze z certyfikatem self-signed pozostaw włączone **Ignore SSL certificate errors**. |
| Overlay się nie pojawia | Zamknij grę, kliknij **Install / repair mod** (uzupełni brakujący overlay), sprawdź `BepInEx\LogOutput.log`. |
| Launcher nie startuje | Zajrzyj do `%APPDATA%\StarTruckMP\launcher-crash.log`. |

## Odinstalowanie

- Instalator usuwa launcher, skróty oraz `%APPDATA%\StarTruckMP`.
- Pliki moda w folderze gry usuwa się ręcznie: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, katalog `BepInEx`.
  (Usunięcie `winhttp.dll` wystarcza, żeby gra startowała bez moda.)
