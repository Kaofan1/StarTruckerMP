# Self-hosted runner i build klienta/serwera

Workflow `.github/workflows/main.yml` ma dwa joby:

| Job | Runner | Co produkuje |
| --- | --- | --- |
| `launcher` | `windows-latest` (hostowany przez GitHub) | launcher + `StarTruckerMP-Setup.exe` → release |
| `build` | `self-hosted, docker, dotnet10` | klient BepInEx, overlay, serwer (win / linux / docker) → release |

Job `launcher` nie potrzebuje żadnej infrastruktury - działa zawsze.

Job `build` jest **opt-in**. Uruchomi się tylko wtedy, gdy jednocześnie:

1. w repozytorium istnieje zmienna `ENABLE_SELF_HOSTED_BUILD` ustawiona na `true`,
2. zarejestrowany jest runner z etykietami `self-hosted`, `docker`, `dotnet10`.

Bez tego job jest pomijany (`skipped`), a release zawiera wyłącznie launcher i instalator.

## Dlaczego potrzebny jest osobny runner

`StarTruckerMP.Client` kompiluje się przeciwko plikom wyciągniętym z gry (`Assembly-CSharp.dll`,
`UnityEngine.*`, `Il2Cppmscorlib.dll` itd.), których nie ma i nie może być w repozytorium.
Job oczekuje ich pod ścieżką `/dlls`:

- `/dlls/core` - `0Harmony.dll`, `BepInEx.Core.dll`, `BepInEx.Preloader.Core.dll`,
  `BepInEx.Unity.Common.dll`, `BepInEx.Unity.IL2CPP.dll`, `Il2CppInterop.Runtime.dll`
- `/dlls/interop` - `Assembly-CSharp.dll`, `Il2Cppmscorlib.dll`, `UnityEngine.dll`,
  `UnityEngine.CoreModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.UI.dll`,
  `UnityEngine.UIModule.dll`, `UnityEngine.UIElementsModule.dll`,
  `UnityEngine.UIElementsNativeModule.dll`, `UnityEngine.AssetBundleModule.dll`,
  `UnityEngine.InputModule.dll`, `UnityEngine.InputLegacyModule.dll`,
  `UnityEngine.PhysicsModule.dll`, `UnityEngine.Physics2DModule.dll`,
  `UnityEngine.TextRenderingModule.dll`, `Unity.TextMeshPro.dll`, `Unity.Addressables.dll`,
  `com.rlabrecque.steamworks.net.dll` oraz opcjonalnie `XGamingRuntime.dll`

Ścieżkę można zmienić w `main.yml` (`env: StarTruckBepInEx` / `StarTruckBepInExSteam`).

Katalog `interop` powstaje sam przy pierwszym uruchomieniu gry z zainstalowanym BepInEx 6
IL2CPP - loader generuje te biblioteki z `GameAssembly.dll` jeszcze przed wczytaniem scen.

## Build bez Xbox (wersja Steam)

`XGamingRuntime.dll` istnieje wyłącznie w wersji Game Pass, więc wersja Steam nie ma z czego
generować jego interopu. Gdy pliku brakuje, projekt kompiluje się ze stałą `STARTRUCKER_NO_XBOX`:
`XboxAuthManager` jest wtedy pustą atrapą, a plugin i tak pomija logowanie Xbox w czasie działania
(sprawdza obecność assembly `XGamingRuntime`). Reszta moda działa bez zmian.

Paczkę klienta da się więc zbudować na komputerze z samą wersją Steam:

```bash
cd StarTruckerMP.Server/overlay-ui && npm ci && npm run build && cd ../..
dotnet build StarTruckerMP.Overlay.Host/StarTruckerMP.Overlay.Host.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true
StarTruckBepInEx='C:\Program Files (x86)\Steam\steamapps\common\Star Trucker\BepInEx' \
dotnet publish StarTruckerMP.Client/StarTruckerMP.Client.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true -o artifacts/client
powershell -NoProfile -Command "Compress-Archive -Path artifacts/client/* -DestinationPath StarTruckerMP-bepinex-plugin-win-x64.zip -Force"
```

Gotowy `StarTruckerMP-bepinex-plugin-win-x64.zip` wystarczy dodać jako asset wydania - launcher
szuka właśnie tej nazwy.

## Szybka ścieżka: jedna komenda

Repo zawiera gotowy obraz i skrypt: `ci/runner/Dockerfile` oraz `ci/runner/install-runner.sh`.

```bash
git clone https://github.com/Kaofan1/StarTruckerMP && cd StarTruckerMP

# host z Dockerem (Linux albo WSL2); token pobierze się z zalogowanego gh
./ci/runner/install-runner.sh --dlls /srv/startrucker/dlls
```

Skrypt:

1. sprawdza Dockera oraz obecność `/dlls/interop/Assembly-CSharp.dll` i `/dlls/core/0Harmony.dll`,
2. buduje obraz `startrucker-runner:local` (.NET 10 SDK, Node.js 20, `zip`, `git`, `libicu`),
3. rejestruje runner z etykietami `docker,dotnet10`,
4. montuje katalog DLL **tylko do odczytu** pod `/dlls` i ustawia `--restart unless-stopped`,
5. sprawdza w kontenerze `dotnet`, `node`, `npm`, `zip` i obecność DLL,
6. ustawia `ENABLE_SELF_HOSTED_BUILD=true` przez `gh variable set`.

| Opcja | Znaczenie |
| --- | --- |
| `--dlls PATH` | katalog z `core/` i `interop/` (domyślnie `/srv/startrucker/dlls`) |
| `--name NAME` | nazwa runnera i kontenera (domyślnie `startrucker-docker`) |
| `--image TAG` | tag obrazu (domyślnie `startrucker-runner:local`) |
| `--no-variable` | nie zmieniaj zmiennej repozytorium |
| `--skip-dlls-check` | start mimo niekompletnego `/dlls` |
| `--down` | usuń kontener runnera |

Bez `gh` podaj token ręcznie (jednorazowy, ważny około godziny):

```bash
RUNNER_TOKEN=<token> ./ci/runner/install-runner.sh --dlls /srv/startrucker/dlls --no-variable
```

Po `--down` trzeba wygenerować nowy token - rejestracja jest jednorazowa. Poniższe kroki 1–3
opisują wariant ręczny, bez Dockera na hoście.

## 1. Rejestracja runnera (wariant ręczny)

GitHub → repozytorium → **Settings → Actions → Runners → New self-hosted runner → Linux X64**.
GitHub pokaże komendy z aktualną wersją runnera - skopiuj je; poniżej ich odpowiednik:

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner
curl -o actions-runner-linux-x64.tar.gz -L \
  https://github.com/actions/runner/releases/download/v<WERSJA>/actions-runner-linux-x64-<WERSJA>.tar.gz
tar xzf actions-runner-linux-x64.tar.gz

# token z ekranu "New self-hosted runner" (ważny ~1 godzinę)
./config.sh \
  --url https://github.com/Kaofan1/StarTruckerMP \
  --token <TOKEN> \
  --name startrucker-docker \
  --labels docker,dotnet10 \
  --work _work
```

Etykieta `self-hosted` jest dodawana automatycznie. **`docker,dotnet10` są obowiązkowe**,
bo workflow ma `runs-on: [self-hosted, docker, dotnet10]`.

Uruchomienie jako usługa (przetrwa restart maszyny):

```bash
sudo ./svc.sh install "$USER"
sudo ./svc.sh start
sudo ./svc.sh status
```

W Settings → Actions → Runners runner powinien być widoczny jako **Idle** z trzema etykietami.

## 2. Narzędzia wymagane na runnerze

Job używa `bash`, `rm -rf`, `zip`, `npm`, `dotnet` (SDK, nie tylko runtime):

```bash
# .NET 10 SDK (bez uprawnień root, do ~/.dotnet)
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0

# Node.js 20+ (npm ci wymaga też package-lock.json z repo)
node --version && npm --version

# zip i git
sudo apt-get update && sudo apt-get install -y zip unzip git curl
```

Dodaj `~/.dotnet` do `PATH` użytkownika runners (np. w `~/.profile`), inaczej `dotnet` nie
będzie widoczny dla usługi:

```bash
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.profile
echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> ~/.profile
```

Weryfikacja:

```bash
dotnet --list-sdks   # musi zawierać 10.x
npm --version
zip --version | head -2
```

## 3. Katalog `/dlls`

Skopiuj z maszyny z zainstalowanym BepInEx w grze (katalog `BepInEx` w folderze Star Trucker)
podkatalogi `core` i `interop`:

```bash
sudo mkdir -p /dlls
sudo cp -r "/sciezka/do/Star Trucker/BepInEx/core"    /dlls/
sudo cp -r "/sciezka/do/Star Trucker/BepInEx/interop" /dlls/
sudo chmod -R a+rX /dlls

test -f /dlls/interop/Assembly-CSharp.dll && echo OK
test -f /dlls/core/0Harmony.dll && echo OK
```

Jeśli runner działa w kontenerze (np. `myoung34/github-runner`), zamontuj ten katalog do
kontenera pod `/dlls` i ustaw etykiety przez `RUNNER_LABELS=docker,dotnet10`:

```bash
docker run -d --restart always \
  --name startrucker-runner \
  -e REPO_URL=https://github.com/Kaofan1/StarTruckerMP \
  -e RUNNER_TOKEN=<TOKEN> \
  -e RUNNER_NAME=startrucker-docker \
  -e RUNNER_LABELS=docker,dotnet10 \
  -v /srv/startrucker/dlls:/dlls:ro \
  myoung34/github-runner:latest
```

W tym wariancie .NET SDK i Node instalujesz w obrazie (albo użyj obrazu z tymi narzędziami),
a `/dlls` musi być widoczne **wewnątrz** kontenera.

## 4. Zmienna `ENABLE_SELF_HOSTED_BUILD`

Bez niej job `build` jest pomijany, nawet gdy runner jest online. `ci/runner/install-runner.sh`
ustawia ją automatycznie, jeśli `gh` jest zalogowane (chyba że użyjesz `--no-variable`).

**Interfejs:** Settings → **Secrets and variables → Actions → zakładka Variables** →
**New repository variable**:

- Name: `ENABLE_SELF_HOSTED_BUILD`
- Value: `true`

**CLI (gh):**

```bash
gh variable set ENABLE_SELF_HOSTED_BUILD --repo Kaofan1/StarTruckerMP --body true
gh variable list --repo Kaofan1/StarTruckerMP
```

Uwagi:

- to **zmienna**, nie sekret - wartość jest widoczna w logach i w UI, i tak ma być;
- zmiana działa od następnego uruchomienia workflow, bez commita;
- ustawienie `true` bez zarejestrowanego runnera przywraca stary objaw: job wisi w `queued`
  i blokuje zakończenie całego przebiegu. Wtedy albo podłącz runner, albo ustaw `false`.

Wyłączenie:

```bash
gh variable set ENABLE_SELF_HOSTED_BUILD --repo Kaofan1/StarTruckerMP --body false
```

## 5. Environment `production`

Job `build` ma `environment: production`. Jeśli w Settings → **Environments** nie ma takiego
środowiska, utwórz je (New environment → `production`). Możesz dodać required reviewers -
wtedy każde wydanie serwera będzie czekać na zatwierdzenie.

Jeśli nie chcesz żadnej bramki, usuń linię `environment: production` z `main.yml`.

## 6. Pierwszy przebieg i weryfikacja

1. Settings → Actions → Runners: runner **Idle**, etykiety `self-hosted`, `docker`, `dotnet10`.
2. Actions → **Build & Release (alpha)** → **Run workflow** (gałąź `main`).
3. Oczekiwany wynik: `launcher` ✅, `build` ✅.
4. Release `alpha-v<N>` powinien zawierać 6 plików:
   - `StarTruckerMP-Setup.exe`
   - `StarTruckerMP-Launcher-win-x64.zip`
   - `StarTruckerMP-bepinex-plugin-win-x64.zip`
   - `StarTruckerMP-server-win-x64.zip`
   - `StarTruckerMP-server-linux-x64.zip`
   - `StarTruckerMP-server-docker.zip`

Szybka kontrola z terminala:

```bash
gh run list --repo Kaofan1/StarTruckerMP -L 3
gh release view alpha-v<N> --repo Kaofan1/StarTruckerMP
```

## 7. Inne etykiety lub inna ścieżka DLL

- inne etykiety runnera → zmień `runs-on: [self-hosted, docker, dotnet10]` w `main.yml`
  i w `config.sh --labels ...`,
- DLL-e w innym miejscu → zmień `StarTruckBepInEx` i `StarTruckBepInExSteam` w `env:` joba `build`.

## 8. Typowe problemy

| Objaw | Przyczyna | Rozwiązanie |
| --- | --- | --- |
| job `build` wisi w `queued`, `runner_name` puste | brak runnera z właściwymi etykietami lub runner offline | sprawdź Settings → Actions → Runners; uruchom `./run.sh` lub `svc.sh start` |
| `build` jest `skipped` | brak zmiennej `ENABLE_SELF_HOSTED_BUILD=true` | ustaw zmienną (punkt 4) |
| „Waiting for approval” | environment `production` ma required reviewers | zatwierdź w Actions albo usuń bramkę |
| `MSB3245` / „could not resolve” dla `Assembly-CSharp` | brak plików w `/dlls/interop` lub zła ścieżka | uzupełnij `/dlls`, sprawdź `env:` |
| `zip: command not found` | brak pakietu `zip` | `sudo apt-get install -y zip` |
| `npm ci` kończy się błędem | brak Node 20+ / brak `package-lock.json` | zainstaluj Node 20+, nie usuwaj lockfile |
| `Permission denied` przy `/dlls` | złe uprawnienia lub brak montowania w kontenerze | `chmod -R a+rX /dlls`, sprawdź `-v ...:/dlls:ro` |
| brak miejsca na dysku | build pobiera NuGet + Node modules | zwolnij miejsce; joby używają `artifacts/` w `_work` |

## 9. Bezpieczeństwo

Runner zarejestrowany na poziomie repozytorium wykonuje kod z tego repozytorium na Twojej
maszynie. Nie zmieniaj workflow tak, aby uruchamiał self-hosted joby na kodzie z pull requestów
z forków - wtedy niepowołany kod może trafić na hosta z dostępem do `/dlls` i sieci.
