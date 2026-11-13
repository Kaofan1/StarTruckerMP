# Hosting serwera StarTruckerMP

[English](README.md) | **Polski**

Dedykowany serwer trzyma stan graczy, weryfikuje bilety Steam i Xbox oraz synchronizuje ciężarówki,
naczepy, ładunek i sektory. Buduje się na Windows, Linux i Docker.

> [!CAUTION]
> To szybka implementacja, żeby serwery w ogóle działały. To **NIE** jest wersja produkcyjna:
> spodziewaj się błędów i crashy, nie wystawiaj jej do internetu bez przeglądu firewalla.

## Wymagania

- Windows 10/11 x64 albo host Linux x64
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) dla builda framework-dependent
- **UDP i TCP port 7777** osiągalny dla graczy (patrz [Porty](#porty))
- Star Trucker na Steam, żeby wygenerować klucz Steam Web API

## Windows lub Linux (zip)

1. Pobierz `StarTruckerMP-server-win-x64.zip` albo `StarTruckerMP-server-linux-x64.zip` z
   [Releases](https://github.com/Kaofan1/StarTruckerMP/releases).
2. Rozpakuj do folderu, w którym serwer może pisać.
3. Zainstaluj .NET 10 runtime (pomiń przy buildzie Docker).
4. Uruchom `StarTruckerMP.Server.exe` (Windows) albo `dotnet StarTruckerMP.Server.dll` (Linux).
5. Pierwszy start tworzy `server.json` obok pliku wykonywalnego. Wyedytuj go i zrestartuj serwer.

Na Linuksie ten sam build działa jako usługa systemd (`UseSystemd()`), a na Windows jako usługa
`sc.exe` (`UseWindowsService()`).

## Docker

1. Pobierz `StarTruckerMP-server-docker.zip` z Releases.
2. Rozpakuj i otwórz terminal w tym folderze.
3. Uruchom `docker compose up -d --build`.
4. Udostępnij port `7777` dla **TCP i UDP**; plik compose robi to już domyślnie.

Dołączony `docs/server-hosting/Dockerfile` uruchamia `dotnet StarTruckerMP.Server.dll` i wystawia oba
protokoły. Zamontuj `server.json` jako wolumen, żeby ustawienia przetrwały przebudowę kontenera.

## Porty

| Protokół | Port | Zastosowanie |
| --- | --- | --- |
| UDP | 7777 | ruch gry (LiteNetLib) |
| TCP | 7777 | HTTPS API do logowania i overlaya (Kestrel) |

Oba listenery dzielą ten sam numer portu celowo: LiteNetLib używa UDP, Kestrel TCP, więc jedna reguła
firewalla wystarcza dla całego serwera.

## Strona statusu

Serwer odpowiada pod `https://<serwer>:7777/` małą stroną HTML: czy działa, wersją serwera i protokołu,
liczbą podłączonych graczy oraz sektorami, w których są. Strona odświeża się co 5 sekund i dopasowuje
język do przeglądarki (angielski lub polski). Działa też jako health check dla monitoringu uptime.

`0.0.0.0` w logu serwera to tylko adres, na którym nasłuchują oba listenery, więc nie da się go otworzyć
w przeglądarce i pokazuje `ERR_ADDRESS_INVALID`. Wpisuj IP serwera, `127.0.0.1` albo `localhost`.
Certyfikat jest samopodpisany, więc przy pierwszej wizycie przeglądarka poprosi o potwierdzenie.

Bez tokenu sesji strona pokazuje tylko wartości zbiorcze, więc każdy, kto ma dostęp do portu, widzi
sam skrót: żadnych nicków, adresów IP ani tokenów. Formularz na stronie przyjmuje token sesji i wtedy
wyświetla podłączonych graczy z nickiem, sektorem i ID gracza. Token trzymany jest w ciasteczku
HttpOnly z `SameSite=Strict`, a **Ukryj nicki** je usuwa; skrypty mogą go podać w nagłówku
`X-Session-Token`.

Token weźmiesz z logu gry swojego klienta: wypisuje go w linii `[Auth] Token:`, a ważny jest przez
`ApiTokenLifetime`. UI overlaya zostaje pod `/overlay` i wymaga tego samego tokenu, więc wejście tam
bez niego zwraca `401`.

## server.json

Plik powstaje przy pierwszym starcie i jest przepisywany przy każdym uruchomieniu, więc nowe pola
zawsze się pojawiają:

| Klucz | Domyślnie | Znaczenie |
| --- | --- | --- |
| `IpAddress` | `0.0.0.0` | adres, na którym nasłuchują oba listenery |
| `Port` | `7777` | port UDP gry i port TCP HTTPS |
| `MaxPlayers` | `100` | limit graczy |
| `ApiAdminUsername` | `admin` | nazwa administratora HTTP API |
| `ApiAdminPassword` | `changeme` | hasło HTTP API, **zmień je** |
| `ApiTokenLifetime` | `08:00:00` | czas ważności tokenu API |
| `XboxRequiredTitleId` | `0x6E2BD7CD` | wymagany aktywny tytuł Xbox; `0` wyłącza sprawdzanie |
| `SteamWebApiKey` | puste | klucz Steam Web API; gdy puste, bilety Steam **nie** są kryptograficznie weryfikowane |
| `SteamAppId` | `2380050` | App ID Star Trucker używane do weryfikacji biletów |
| `CertificatePath` | `certs/localhost.pfx` | certyfikat HTTPS dla API |
| `CertificatePassword` | `changeit` | hasło tego certyfikatu |

### Klucz Steam Web API

Wygeneruj klucz na <https://steamcommunity.com/dev/apikey> i wpisz go w `SteamWebApiKey`. Z kluczem
serwer wywołuje `ISteamUserAuth/AuthenticateUserTicket` i odrzuca bilety spoza Star Trucker. Bez klucza
każdy może podać się za dowolne konto Steam, więc publiczny serwer nigdy nie powinien działać bez niego.

### Certyfikaty

Gdy `CertificatePath` nie istnieje, serwer generuje self-signed certyfikat dla `localhost` ważny rok
i zapisuje go jako PFX. Gracze łączący się z takim serwerem muszą włączyć **Ignore SSL certificate
errors** w launcherze, albo zainstaluj prawdziwy certyfikat i wskaż go w `CertificatePath` oraz
`CertificatePassword`.

## Podłączanie graczy

1. Zainstaluj launcher z Releases i otwórz kartę **Zakładki serwerów**.
2. Kliknij **Dodaj**, wpisz nazwę, IP lub host serwera i port `7777`, potem **Zapisz serwer**.
3. Kliknij **Zainstaluj / napraw mod**, a następnie **GRAJ**. Launcher zapisuje adres do
   `BepInEx/config/StarTruckerMP.Client.cfg` przed każdym uruchomieniem.

Gracze instalujący ręcznie edytują `[Connection] ServerAddress` i `ServerPort` w tym pliku.

## Rozwiązywanie problemów

| Objaw | Przyczyna | Rozwiązanie |
| --- | --- | --- |
| Gracze dostają timeout | zablokowane UDP 7777 | otwórz UDP 7777 w firewallu i panelu hostingu |
| Launcher pokazuje błąd certyfikatu | self-signed certyfikat | włącz **Ignore SSL certificate errors** dla tego serwera |
| `server.json` wraca do domyślnych | plik był niepoprawnym JSON-em | popraw JSON, serwer przepisuje go przy starcie |
| Każdy wchodzi na dowolne konto Steam | puste `SteamWebApiKey` | wygeneruj klucz i zrestartuj serwer |
| Port zajęty | inna instancja lub usługa | zatrzymaj tamten proces albo zmień `Port` w `server.json` |
| Kontener Dockera restartuje się w pętli | brak praw do `server.json` lub złe mapowanie portów | sprawdź `docker logs`, zamontuj zapisywalny wolumen |
