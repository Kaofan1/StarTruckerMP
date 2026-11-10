# StarTruckerMP server hosting

**English** | [Polski](README.pl.md)

The dedicated server keeps player state, validates Steam and Xbox tickets and synchronizes trucks,
trailers, cargo and sectors. It builds for Windows, Linux and Docker.

> [!CAUTION]
> This is a quick and dirty implementation to get servers up and running. It is **NOT** intended for
> production: expect bugs and crashes, and do not expose it to the internet without a firewall review.

## Requirements

- Windows 10/11 x64 or a Linux x64 host
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) when using the framework-dependent build
- **UDP and TCP port 7777** reachable by players (see [Ports](#ports))
- A Star Trucker copy on Steam to generate the Steam Web API key

## Windows or Linux (zip build)

1. Download `StarTruckerMP-server-win-x64.zip` or `StarTruckerMP-server-linux-x64.zip` from
   [Releases](https://github.com/Kaofan1/StarTruckerMP/releases).
2. Extract it into a folder that the server may write to.
3. Install the .NET 10 runtime (skip this for the Docker build).
4. Start `StarTruckerMP.Server.exe` (Windows) or `dotnet StarTruckerMP.Server.dll` (Linux).
5. The first start creates `server.json` next to the executable. Edit it and restart the server.

On Linux the server also installs itself as a systemd service when started by systemd
(`UseSystemd()`); on Windows the same build supports `sc.exe` service hosting (`UseWindowsService()`).

## Docker

1. Download `StarTruckerMP-server-docker.zip` from Releases.
2. Unzip it and open a terminal in that folder.
3. Run `docker compose up -d --build`.
4. Publish port `7777` for both **TCP and UDP**; the compose file already does this.

The bundled `docs/server-hosting/Dockerfile` runs `dotnet StarTruckerMP.Server.dll` and exposes both
protocols. Mount `server.json` as a volume so settings survive a container rebuild.

## Ports

| Protocol | Port | Purpose |
| --- | --- | --- |
| UDP | 7777 | game traffic (LiteNetLib) |
| TCP | 7777 | HTTPS API for authentication and the overlay (Kestrel) |

Both listeners share the same port number on purpose: LiteNetLib uses UDP, Kestrel uses TCP, so one
firewall rule covers the whole server.

## server.json

The file is created on first start and rewritten on every start, so new fields always appear:

| Key | Default | Meaning |
| --- | --- | --- |
| `IpAddress` | `0.0.0.0` | address both listeners bind to |
| `Port` | `7777` | UDP game port and TCP HTTPS port |
| `MaxPlayers` | `100` | player limit |
| `ApiAdminUsername` | `admin` | HTTP API administrator name |
| `ApiAdminPassword` | `changeme` | HTTP API password, **change this** |
| `ApiTokenLifetime` | `08:00:00` | how long an API token stays valid |
| `XboxRequiredTitleId` | `0x6E2BD7CD` | Xbox title that must be active; `0` disables the check |
| `SteamWebApiKey` | empty | Steam Web API key; when empty, Steam tickets are **not** cryptographically validated |
| `SteamAppId` | `2380050` | Star Trucker App ID used for ticket validation |
| `CertificatePath` | `certs/localhost.pfx` | HTTPS certificate used by the API |
| `CertificatePassword` | `changeit` | password of that certificate |

### Steam Web API key

Get a key at <https://steamcommunity.com/dev/apikey> and put it into `SteamWebApiKey`. With a key the
server calls `ISteamUserAuth/AuthenticateUserTicket` and rejects tickets that do not belong to Star
Trucker. Without a key anyone can claim any Steam identity, so never run a public server without it.

### Certificates

When `CertificatePath` does not exist, the server generates a self-signed certificate for `localhost`
valid for one year and saves it as a PFX. Players connecting to such a server must enable
**Ignore SSL certificate errors** in the launcher, or you can install a real certificate and point
`CertificatePath` and `CertificatePassword` at it.

## Connecting players

1. Install the launcher from Releases and open the **Server bookmarks** card.
2. Click **Add**, enter a name, the server IP or host name and port `7777`, then **Save server**.
3. Click **Install / repair mod**, then **PLAY**. The launcher writes the address into
   `BepInEx/config/StarTruckerMP.Client.cfg` before every launch.

Manual players edit `[Connection] ServerAddress` and `ServerPort` in that file instead.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Players time out | UDP 7777 blocked | open UDP 7777 in the firewall and the host panel |
| Launcher shows a certificate error | self-signed certificate | enable **Ignore SSL certificate errors** for that server |
| `server.json` is recreated with defaults | the file was unreadable JSON | fix the JSON, the server rewrites it on every start |
| Everyone can join with any Steam account | `SteamWebApiKey` is empty | create a key and restart the server |
| Port already in use | another instance or service | stop the other process or change `Port` in `server.json` |
| Docker container restarts in a loop | missing `server.json` permissions or bad port mapping | check `docker logs`, mount a writable volume |
