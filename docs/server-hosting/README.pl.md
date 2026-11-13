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

## Skrypty instalacyjne

Repozytorium zawiera instalator dla obu systemów. Pobiera on paczkę serwera z najnowszego wydania,
zostawia istniejący `server.json` i folder `certs`, zapisuje konfigurację z Twoim portem i losowym
hasłem administratora API, a potem rejestruje usługę i ją uruchamia.

Windows, w PowerShellu z uprawnieniami administratora:

```
powershell -ExecutionPolicy Bypass -File .\install-server-windows.ps1 -Port 7777
```

Linux, jako root:

```
sudo ./install-server-linux.sh --port 7777
```

`-Version` lub `--version` wskazuje tag wydania, `-InstallDir` lub `--dir` zmienia folder
(`C:\Program Files\StarTruckerMP Server`, `/opt/startruckermp`), `-MaxPlayers` lub `--max-players`
ustawia limit graczy, a `-AdminPassword` hasło API na Windows (Linux generuje je i zapisuje w
`server.json`). Skrypty tworzą usługę `StarTruckerMPServer` na Windows albo `startruckermp.service`
na Linuksie. Windows dodaje reguły firewalla; na Linuksie otwórz TCP i UDP `7777` sam, skrypt wypisuje
gotowe polecenia `ufw` i `firewall-cmd`.

Serwer szuka `server.json`, `certs` i `wwwroot` obok swojego pliku wykonywalnego, więc usługa działa
niezależnie od katalogu, z którego została uruchomiona.

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
| `DirectoryUrl` | puste | adres huba katalogu, np. `https://hub.example:7777`; puste wyłącza zgłaszanie |
| `DirectoryHost` | puste | publiczne IP lub domena, na którą łączą się gracze; puste bierze adres nasłuchu lub LAN IPv4 |
| `DirectoryName` | puste | nazwa w wyszukiwarce launchera; puste pokazuje `StarTruckerMP server` |
| `DirectoryDescription` | puste | jednolinijkowy opis we wpisie katalogu |

### Publikowanie serwera w wyszukiwarce launchera

Launcher czyta publiczny katalog, który może hostować dowolny serwer (`/directory/servers`). Serwer
dedykowany dopisuje się do tej listy sam: ustaw `DirectoryUrl` na adres huba, np.
`https://hub.example:7777`, a zgłosi się zaraz po starcie i potem raz na minutę. Wpis wygasa po
trzech minutach bez heartbeatu, więc zatrzymany serwer znika sam, a wpis pokazuje aktualną liczbę
graczy i limit.

W `DirectoryHost` wpisz adres, którego mają używać gracze, czyli publiczne IP albo domenę. Gdy jest
puste, serwer podaje adres, na którym nasłuchuje, albo pierwszy LAN IPv4 przy `0.0.0.0`, a taki adres
działa tylko w sieci lokalnej. Log serwera wypisuje jedną linię, gdy wpis jest przyjęty, i zmienia ją
tylko przy zmianie stanu, więc hub, który leży, nie zapełnia logu.

Hub może działać na self-signed certyfikacie, który serwer tworzy sam, bo wymieniana jest tylko
publiczna lista. Po restarcie hub traci listę z pamięci; każdy serwer dostaje odrzucony token
i rejestruje się ponownie przy następnym heartbeacie.

### Klucz Steam Web API

Wygeneruj klucz na <https://steamcommunity.com/dev/apikey> i wpisz go w `SteamWebApiKey`. Z kluczem
serwer wywołuje `ISteamUserAuth/AuthenticateUserTicket` i odrzuca bilety spoza Star Trucker. Bez klucza
każdy może podać się za dowolne konto Steam, więc publiczny serwer nigdy nie powinien działać bez niego.

### Certyfikaty

Gdy `CertificatePath` nie istnieje, serwer generuje self-signed certyfikat dla `localhost` ważny rok
i zapisuje go jako PFX. Gracze łączący się z takim serwerem muszą włączyć **Ignore SSL certificate
errors** w launcherze, albo zainstaluj prawdziwy certyfikat i wskaż go w `CertificatePath` oraz
`CertificatePassword`.

## Katalog serwerów dedykowanych

Każda instancja serwera może hostować publiczny katalog, którego szuka launcher. Serwer dedykowany
utrzymuje się na liście heartbeatem; pierwsze wywołanie zwraca id i token, a kolejne muszą je podawać.

```
POST /directory/announce
{"name":"EU Freight","host":"203.0.113.10","port":7777,"players":3,"capacity":8,
 "description":"wieczorami, EU","protocol":"1","version":"1.0.64"}

200 {"id":"...","token":"...","ttlSeconds":180}
```

Heartbeat wysyłaj co 60 sekund z tym samym `id` i `token`, aktualizując `players` w miarę zapełniania
sesji. Wpis wygasa po trzech minutach bez heartbeatu, pierwsze zgłoszenie z danego adresu jest
przyjmowane raz na sekundę, a zły token kończy się kodem 403, więc nie da się przejąć cudzego wpisu.

`GET /directory/servers?q=freight` zwraca publiczną listę (`name`, `host`, `port`, `players`,
`capacity`, `description`, `lastSeenSeconds`); to samo pokazuje przeglądarka **SERWERY DEDYKOWANE**
w launcherze. Kolumnę PING launcher wypełnia sam, mierząc czas odpowiedzi na `host:port`. Katalog trzyma wpisy w pamięci, więc po restarcie huba serwery zarejestrują się ponownie
następnym heartbeatem i dostaną nowe id.

## Podłączanie graczy

1. Zainstaluj launcher z Releases i otwórz kartę **Zakładki serwerów**.
2. Kliknij **Dodaj**, wpisz nazwę, IP lub host serwera i port `7777`, potem **Zapisz serwer**.
3. Kliknij **Zainstaluj**, a następnie **GRAJ**. Launcher zapisuje adres do
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
