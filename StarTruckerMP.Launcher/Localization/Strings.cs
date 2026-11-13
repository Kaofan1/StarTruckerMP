using System;

namespace StarTruckerMP.Launcher.Localization;

/// <summary>Languages the launcher UI is translated into.</summary>
internal enum AppLanguage
{
    English,
    Polish
}

/// <summary>
/// Single place for every user-facing string. Each member returns the text for the
/// language currently selected, so UI construction stays plain code with no bindings.
/// </summary>
internal static class Strings
{
    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static void Set(AppLanguage language) => Current = language;

    private static string T(string english, string polish) =>
        Current == AppLanguage.Polish ? polish : english;

    // ----- language selector -----

    public static string LanguageName(AppLanguage language) =>
        language == AppLanguage.Polish ? "Polski" : "English";

    public static string Flag(AppLanguage language) =>
        language == AppLanguage.Polish ? "\U0001F1F5\U0001F1F1" : "\U0001F1EC\U0001F1E7";

    public static string StorageCode(AppLanguage language) =>
        language == AppLanguage.Polish ? "pl" : "en";

    public static AppLanguage Parse(string? code) =>
        string.Equals(code, "pl", StringComparison.OrdinalIgnoreCase) ? AppLanguage.Polish : AppLanguage.English;

    public static string LanguageSwitched(AppLanguage language) =>
        language == AppLanguage.Polish ? "Zmieniono język na polski." : "Language switched to English.";

    public static string LanguageActive(AppLanguage language) =>
        language == AppLanguage.Polish ? "Język: polski" : "Language: English";

    // ----- header -----

    public static string Subtitle => T("Launcher · mod installer · server bookmarks", "Launcher · instalator moda · zakładki serwerów");

    public static string Standby => T("Standby", "Gotowy");

    public static string UpdateAvailable(string release) =>
        T($"Update available · {release}", $"Dostępna aktualizacja · {release}");

    // ----- buttons -----

    public static string DetectGame => T("Detect game", "Wykryj grę");

    public static string ChooseFolder => T("Choose folder…", "Wybierz folder…");

    public static string OpenGameFolder => T("Open game folder", "Otwórz folder gry");

    public static string OpenConfig => T("Open config", "Otwórz konfigurację");

    public static string Play => T("PLAY", "GRAJ");

    public static string Add => T("Add", "Dodaj");

    public static string Remove => T("Remove", "Usuń");

    public static string SaveServer => T("Save server", "Zapisz serwer");

    // ----- inputs -----

    public static string ServerNameWatermark => T("Server name", "Nazwa serwera");

    public static string HostWatermark => T("host or IP", "host lub IP");

    public static string PortWatermark => T("port", "port");

    public static string MicrophoneWatermark => T("leave empty for the system default", "puste = domyślne urządzenie");

    public static string MicSystemDefault => T("System default (Windows)", "Domyślne urządzenie systemowe");

    public static string MicRefresh => T("Refresh", "Odśwież");

    public static string FieldName => T("Name", "Nazwa");

    public static string FieldHost => T("Host", "Host");

    public static string FieldPort => T("Port", "Port");

    public static string FieldMicrophone => T("Microphone", "Mikrofon");

    public static string LaunchThroughSteam =>
        T("Launch through Steam (recommended - keeps Steam login working)",
            "Uruchamiaj przez Steam (zalecane - logowanie Steam działa normalnie)");

    public static string IgnoreSsl =>
        T("Ignore SSL certificate errors (needed for self-signed servers)",
            "Ignoruj błędy certyfikatu SSL (potrzebne dla serwerów z własnym certyfikatem)");

    public static string CloseAfterLaunch =>
        T("Close the launcher after the game starts", "Zamknij launcher po starcie gry");

    // ----- cards -----

    public static string CardGame => T("GAME INSTALLATION", "INSTALACJA GRY");

    public static string CardServers => T("SERVER BOOKMARKS", "ZAKŁADKI SERWERÓW");

    public static string CardLaunch => T("LAUNCH OPTIONS", "OPCJE URUCHAMIANIA");

    public static string CardConnection => T("CONNECTION", "POŁĄCZENIE");

    public static string CardModStatus => T("MOD STATUS", "STATUS MODA");

    public static string CardLoader => T("BEPINEX LOADER", "LOADER BEPINEX");

    public static string LoaderNote => T("installed separately from the client", "instalowany osobno od klienta");

    public static string CardShortcuts => T("SHORTCUTS", "SKRÓTY");

    public static string CardLog => T("LAUNCHER LOG", "LOG LAUNCHERA");

    public static string CardLocalServer => T("LOCAL SERVER", "SERWER LOKALNY");

    public static string LocalServerNote =>
        T("lets this PC host the session", "pozwala temu komputerowi hostować sesję");

    public static string InstallAction => T("Install", "Zainstaluj");

    public static string RepairMod => T("Repair mod", "Napraw mod");

    public static string RepairLoader => T("Repair loader", "Napraw loader");

    public static string UninstallAction => T("Uninstall", "Odinstaluj");

    public static string ModAlreadyInstalled =>
        T("The mod is already installed.", "Mod jest już zainstalowany.");

    public static string ModRepairing => T("Repairing the mod…", "Naprawiam mod…");

    public static string ModRepairCompleted => T("The mod was repaired.", "Mod został naprawiony.");

    public static string ModRepairFailed(string reason) =>
        T($"Repair failed: {reason}", $"Naprawa nie powiodła się: {reason}");

    public static string ModUninstalling => T("Uninstalling the mod…", "Odinstalowuję mod…");

    public static string ModUninstallCompleted =>
        T("The mod was removed from the game folder.", "Mod został usunięty z folderu gry.");

    public static string ModUninstallFailed(string reason) =>
        T($"Uninstall failed: {reason}", $"Odinstalowanie nie powiodło się: {reason}");

    public static string ModUninstallSkipped =>
        T("The mod is not installed.", "Mod nie jest zainstalowany.");

    public static string ModUninstallConfirmTitle =>
        T("Remove the mod from the game folder?", "Usunąć moda z folderu gry?");

    public static string ModUninstallConfirm(string gamePath) =>
        T($"The launcher deletes BepInEx\\plugins\\StarTruckerMP from {gamePath}. The game itself and the BepInEx loader stay in place, so you can install the mod again at any time.",
            $"Launcher usunie folder BepInEx\\plugins\\StarTruckerMP z {gamePath}. Gra i loader BepInEx zostaną na miejscu, więc mod można w każdej chwili zainstalować ponownie.");

    public static string LoaderRepairing => T("Repairing the loader…", "Naprawiam loader…");

    public static string LoaderRepairCompleted =>
        T("The BepInEx loader was repaired.", "Loader BepInEx został naprawiony.");

    public static string LoaderRepairFailed(string reason) =>
        T($"Loader repair failed: {reason}", $"Naprawa loadera nie powiodła się: {reason}");

    public static string LoaderUninstalling => T("Removing the loader…", "Usuwam loader…");

    public static string LoaderUninstallCompleted =>
        T("The BepInEx loader was removed.", "Loader BepInEx został usunięty.");

    public static string LoaderUninstallKeptPlugins =>
        T("BepInEx plugins and config were kept.", "Pluginy i konfiguracja BepInEx zostały zachowane.");

    public static string LoaderUninstallFailed(string reason) =>
        T($"Loader uninstall failed: {reason}", $"Odinstalowanie loadera nie powiodło się: {reason}");

    public static string LoaderNotInstalled =>
        T("The BepInEx loader is not installed.", "Loader BepInEx nie jest zainstalowany.");

    public static string LoaderUninstallConfirmTitle =>
        T("Remove the BepInEx loader?", "Usunąć loader BepInEx?");

    public static string LoaderUninstallConfirm(string gamePath) =>
        T($"The launcher deletes winhttp.dll, doorstop_config.ini, .doorstop_version and BepInEx\\core from {gamePath}. Plugins in BepInEx\\plugins and the BepInEx\\config files are kept.",
            $"Launcher usunie winhttp.dll, doorstop_config.ini, .doorstop_version oraz BepInEx\\core z {gamePath}. Pluginy z BepInEx\\plugins i pliki z BepInEx\\config zostaną zachowane.");

    public static string UninstallCancelled =>
        T("Uninstall cancelled.", "Odinstalowanie anulowane.");

    public static string CancelAction => T("Cancel", "Anuluj");

    public static string CardDirectory => T("DEDICATED SERVERS", "SERWERY DEDYKOWANE");

    public static string DirectoryNote => T("search public servers", "szukaj publicznych serwerów");

    public static string DirectoryColumnServer => T("SERVER", "SERWER");

    public static string DirectoryColumnPlayers => T("PLAYERS", "GRACZE");

    public static string DirectoryColumnPing => T("PING", "PING");

    public static string DirectorySearchPlaceholder =>
        T("Server name or host", "Nazwa serwera lub host");

    public static string DirectorySearchAction => T("Search", "Szukaj");

    public static string DirectoryCaption =>
        T("The list comes from the server that hosts the directory. Double click a server to keep it in your server list.",
            "Lista pochodzi z serwera, który hostuje katalog. Kliknij serwer dwukrotnie, żeby zapisać go na liście serwerów.");

    public static string DirectoryNeedsUrl =>
        T("Enter the directory host and port first.", "Najpierw wpisz host i port katalogu.");

    public static string DirectorySearching => T("Searching the directory…", "Szukam w katalogu…");

    public static string DirectoryEmpty =>
        T("No public servers match.", "Brak publicznych serwerów dla tego wyszukiwania.");

    public static string DirectoryFound(int count) =>
        count == 1
            ? T("1 public server", "1 publiczny serwer")
            : T($"{count} public servers", $"{count} publicznych serwerów");

    public static string DirectoryFailed(string reason) =>
        T($"Directory unavailable: {reason}", $"Katalog niedostępny: {reason}");

    public static string DirectoryUnreachable =>
        T("the address did not answer", "adres nie odpowiedział");

    public static string DirectoryPlayers(int players, int capacity) =>
        T($"{players}/{capacity} players", $"{players}/{capacity} graczy");

    public static string DirectoryServerAlreadyAdded(string name) =>
        T($"{name} is already on your server list.", $"{name} jest już na Twojej liście serwerów.");

    public static string DirectoryServerAdded(string name, string address) =>
        T($"{name} ({address}) was added to your servers.", $"{name} ({address}) został dodany do Twoich serwerów.");

    public static string CardPlugins => T("BEPINEX MODS", "MODY BEPINEX");

    public static string PluginsNote => T("other mods and plugins", "inne mody i pluginy");

    public static string PluginsCaption =>
        T("Plugins are loaded when the game starts. Click a plugin to select it, then remove it here.",
            "Pluginy są wczytywane przy starcie gry. Kliknij plugin, aby go zaznaczyć, a potem usuń go tutaj.");

    public static string RemovePluginAction => T("Remove selected", "Usuń zaznaczony");

    public static string EnablePluginAction => T("Enable", "Włącz");

    public static string DisablePluginAction => T("Disable", "Wyłącz");

    public static string PluginDisabledTag => T("disabled", "wyłączony");

    public static string PluginDisabling(string name) =>
        T($"Switching {name} off…", $"Wyłączam {name}…");

    public static string PluginEnabling(string name) =>
        T($"Switching {name} on…", $"Włączam {name}…");

    public static string PluginDisabled(string name) =>
        T($"{name} was switched off. Start the game to check the result.",
            $"{name} został wyłączony. Uruchom grę, aby sprawdzić efekt.");

    public static string PluginEnabled(string name) =>
        T($"{name} was switched on again.", $"{name} został ponownie włączony.");

    public static string PluginToggleFailed(string reason) =>
        T($"Could not switch the plugin: {reason}", $"Nie udało się przełączyć pluginu: {reason}");

    public static string PluginToggleOwnClient =>
        T("The StarTruckerMP client cannot be switched off here; use Uninstall in the mod card.",
            "Klienta StarTruckerMP nie można tu wyłączyć; użyj Odinstaluj w karcie STATUS MODA.");

    public static string PluginDisabledNameTaken(string name) =>
        T($"{name} is already switched off.", $"{name} jest już wyłączony.");

    public static string PluginEnabledNameTaken(string name) =>
        T($"A plugin named {name} is enabled already.", $"Plugin o nazwie {name} jest już włączony.");

    public static string RemovePluginConfirmTitle => T("Remove this plugin?", "Usunąć ten plugin?");

    public static string RemovePluginConfirm(string name, string target) =>
        T($"The launcher deletes {name} from {target}. You can add it again with Add mod… at any time.",
            $"Launcher usunie {name} z {target}. Możesz go dodać ponownie przez Dodaj moda… w każdej chwili.");

    public static string PluginRemoving(string name) =>
        T($"Removing {name}…", $"Usuwam {name}…");

    public static string PluginAlreadyGone(string name) =>
        T($"{name} was already gone.", $"{name} już nie było w folderze.");

    public static string PluginRemoved(string name) =>
        T($"{name} was removed from BepInEx\\plugins.", $"{name} został usunięty z BepInEx\\plugins.");

    public static string PluginRemoveFailed(string reason) =>
        T($"Could not remove the plugin: {reason}", $"Nie udało się usunąć pluginu: {reason}");

    public static string PluginRemoveOwnClient =>
        T("The StarTruckerMP client is managed by the mod card: use Uninstall there.",
            "Klientem StarTruckerMP zarządza karta STATUS MODA: użyj tam przycisku Odinstaluj.");

    public static string PluginOutsidePlugins(string path) =>
        T($"That path is outside BepInEx\\plugins: {path}", $"Ta ścieżka jest poza BepInEx\\plugins: {path}");

    public static string PluginSelectHint =>
        T("Select a plugin above first.", "Najpierw zaznacz plugin na liście.");

    public static string AddPluginAction => T("Add mod…", "Dodaj moda…");

    public static string OpenPluginsFolder => T("Open plugins folder", "Otwórz folder pluginów");

    public static string RefreshPlugins => T("Refresh", "Odśwież");

    public static string PluginsEmpty =>
        T("No plugins in BepInEx\\plugins yet.", "W BepInEx\\plugins nie ma jeszcze żadnych pluginów.");

    public static string PluginsCount(int count) =>
        count == 1
            ? T("1 plugin in BepInEx\\plugins", "1 plugin w BepInEx\\plugins")
            : T($"{count} plugins in BepInEx\\plugins", $"{count} pluginów w BepInEx\\plugins");

    public static string OwnClientTag => T("our client", "nasz klient");

    public static string PluginFolderTag => T("folder", "folder");

    public static string PluginDllTag => T("dll", "dll");

    public static string PluginPickerTitle =>
        T("Select a plugin to add (.dll or .zip)", "Wybierz plugin do dodania (.dll lub .zip)");

    public static string PluginPickerType => T("BepInEx plugin", "Plugin BepInEx");

    public static string PluginInstalling(string name) =>
        T($"Copying {name}…", $"Kopiuję {name}…");

    public static string PluginInstalled(string name, string target) =>
        T($"{name} was added to {target}", $"{name} został dodany do {target}");

    public static string PluginInstallFailed(string reason) =>
        T($"Could not add the plugin: {reason}", $"Nie udało się dodać pluginu: {reason}");

    public static string PluginArchiveUnsafe(string entry) =>
        T($"The archive points outside the plugins folder: {entry}",
            $"Archiwum wskazuje poza folder pluginów: {entry}");

    public static string PluginUnsupported(string name) =>
        T($"{name} is not a plugin: pick a .dll or a .zip file.",
            $"{name} nie jest pluginem: wybierz plik .dll albo .zip.");

    public static string StartLocalServer => T("Start server", "Uruchom serwer");

    public static string StopLocalServer => T("Stop server", "Zatrzymaj serwer");

    public static string LocalServerRunning(int processId) =>
        T($"Running (PID {processId}). The client logs in to this PC.",
            $"Działa (PID {processId}). Klient loguje się na tym komputerze.");

    public static string LocalServerStopped =>
        T("Stopped - without a server the client gets no login and the overlay stays empty.",
            "Zatrzymany - bez serwera klient nie zaloguje się i overlay pozostanie pusty.");

    public static string LocalServerMissing =>
        T("Server files not found yet - start downloads them once.",
            "Brak plików serwera - uruchomienie pobierze je raz.");

    public static string LocalServerCopying(string path) =>
        T($"Copying the bundled server from {path}…", $"Kopiuję dołączony serwer z {path}…");

    public static string LocalServerLookingUp =>
        T("Looking up the server package…", "Szukam paczki serwera…");

    public static string LocalServerDownloading(string name) =>
        T($"Downloading {name}…", $"Pobieram {name}…");

    public static string LocalServerExtracting =>
        T("Extracting the server…", "Rozpakowuję serwer…");

    public static string LocalServerPackageMissing =>
        T("This release has no server package (StarTruckerMP-server-win-x64.zip).",
            "To wydanie nie ma paczki serwera (StarTruckerMP-server-win-x64.zip).");

    public static string LocalServerInstallFailed(string reason) =>
        T($"Could not prepare the server: {reason}", $"Nie udało się przygotować serwera: {reason}");

    public static string LocalServerStartFailed(string reason) =>
        T($"The server did not start: {reason}", $"Serwer nie wystartował: {reason}");

    public static string LocalServerPortHint =>
        T("port 7777 may already be in use", "port 7777 może być już zajęty");

    public static string LocalServerStarted(string path) =>
        T($"Local server started from {path}.", $"Uruchomiono lokalny serwer z {path}.");

    public static string LocalServerStoppedLog => T("Local server stopped.", "Zatrzymano lokalny serwer.");

    public static string LocalServerUpdated(string tag) =>
        T($"Local server updated to {tag}.", $"Zaktualizowano serwer lokalny do {tag}.");

    public static string LocalServerUpdateFailed(string reason) =>
        T($"The local server could not be updated: {reason}", $"Nie udało się zaktualizować serwera lokalnego: {reason}");

    public static string LocalServerStopFailed =>
        T("The server could not be stopped.", "Nie udało się zatrzymać serwera.");

    public static string EditServerConfig =>
        T("Edit server.json", "Edytuj server.json");

    public static string LocalServerConfigMissing =>
        T("server.json does not exist yet. Start the server once so it writes its settings.",
            "server.json jeszcze nie istnieje. Uruchom serwer raz, żeby zapisał ustawienia.");

    public static string LocalServerConfigFailed(string reason) =>
        T($"Could not open server.json: {reason}", $"Nie udało się otworzyć server.json: {reason}");

    public static string LocalServerFolderPath(string path) =>
        T($"Server folder: {path}", $"Folder serwera: {path}");

    public static string LocalServerCaption =>
        T(@"This copy belongs to the launcher: it is downloaded here and updated from GitHub Releases. A server installed by the separate Setup has its own card.",
            @"Ta kopia należy do launchera: tutaj jest pobierana i aktualizowana z GitHub Releases. Serwer z osobnego instalatora ma własną kartę.");

    public static string LocalServerPortTakenByDedicated =>
        T("Port 7777 is held by the dedicated server. Stop it on the DEDICATED SERVER card to start the launcher's copy.",
            "Port 7777 zajmuje serwer dedykowany. Zatrzymaj go na karcie SERWER DEDYKOWANY, żeby uruchomić kopię launchera.");

    public static string CardDedicatedServer => T("DEDICATED SERVER", "SERWER DEDYKOWANY");

    public static string DedicatedServerNote =>
        T("Program Files install, runs as a Windows service",
            "instalacja w Program Files, działa jako usługa Windows");

    public static string DedicatedServerRunning(int processId) =>
        T($"Running (PID {processId}).", $"Działa (PID {processId}).");

    public static string DedicatedServerStopped =>
        T("Stopped. The service is registered and starts with the system.",
            "Zatrzymany. Usługa jest zarejestrowana i startuje z systemem.");

    public static string DedicatedServerMissing =>
        T("Not installed. Run StarTruckerMP-Server-Setup.exe to add it.",
            "Nie zainstalowano. Uruchom StarTruckerMP-Server-Setup.exe, żeby go dodać.");

    public static string DedicatedServerInstallFolder(string path) =>
        T($"Install folder: {path}", $"Folder instalacji: {path}");

    public static string StartDedicatedServer => T("Start service", "Uruchom usługę");

    public static string StopDedicatedServer => T("Stop service", "Zatrzymaj usługę");

    public static string DedicatedServerStartedLog =>
        T("StarTruckerMPServer service started.", "Uruchomiono usługę StarTruckerMPServer.");

    public static string DedicatedServerStoppedLog =>
        T("StarTruckerMPServer service stopped.", "Zatrzymano usługę StarTruckerMPServer.");

    public static string DedicatedServerNeedsAdmin =>
        T("The Windows service needs administrator rights. Accept the UAC prompt or use services.msc.",
            "Usługa Windows wymaga uprawnień administratora. Zaakceptuj pytanie UAC albo użyj services.msc.");

    public static string DedicatedServerTimeout =>
        T("The service did not change state in time. Check services.msc for its current state.",
            "Usługa nie zmieniła stanu w wyznaczonym czasie. Sprawdź jej stan w services.msc.");

    public static string DedicatedServerFailed(string reason) =>
        T($"Could not control the service: {reason}", $"Nie udało się sterować usługą: {reason}");

    public static string DedicatedServerConfigMissing =>
        T(@"%ProgramData%\StarTruckerMP\server.json does not exist. Install the server with Setup first.",
            @"%ProgramData%\StarTruckerMP\server.json nie istnieje. Najpierw zainstaluj serwer przez Setup.");

    public static string DedicatedServerConfigFailed(string reason) =>
        T($"Could not open server.json: {reason}", $"Nie udało się otworzyć server.json: {reason}");

    public static string DedicatedServerCaption =>
        T(@"Settings: %ProgramData%\StarTruckerMP\server.json. Status page: https://localhost:7777/.",
            @"Ustawienia: %ProgramData%\StarTruckerMP\server.json. Strona statusu: https://localhost:7777/.");

    public static string ModNote => T("BepInEx + client + overlay", "BepInEx + klient + overlay");

    public static string GameCaption =>
        T("Detected from your Steam libraries. If that fails, choose the folder that contains Star Trucker.exe.",
            "Wykrywana z bibliotek Steam. Jeśli się nie uda, wskaż folder z plikiem Star Trucker.exe.");

    public static string ServerCaption =>
        T(@"The selected address is written to BepInEx\config\StarTruckerMP.Client.cfg before every launch.",
            @"Wybrany adres jest zapisywany do BepInEx\config\StarTruckerMP.Client.cfg przed każdym uruchomieniem.");

    public static string PlayCaption =>
        T("PLAY starts Star Trucker through Steam, so Steam login keeps working. The overlay appears once you load into the sector - " +
          "the very first launch after installing the mod is slower because BepInEx builds its IL2CPP interop assemblies.",
            "GRAJ uruchamia Star Trucker przez Steam, więc logowanie Steam działa normalnie. Overlay pojawia się po wczytaniu sektora - " +
            "pierwsze uruchomienie po instalacji moda trwa dłużej, bo BepInEx buduje biblioteki interop IL2CPP.");

    public static string ShortcutsCaption =>
        T("Handy for reading BepInEx logs or editing the client config by hand.",
            "Przydatne do czytania logów BepInEx i ręcznej edycji konfiguracji klienta.");

    // ----- mod status -----

    public static string ModUnknown => T("Unknown - the game folder is not set.", "Nieznany - folder gry nie jest ustawiony.");

    public static string ModInstalled =>
        T("Installed (BepInEx 6 IL2CPP + client + overlay). Ready to play.",
            "Zainstalowany (BepInEx 6 IL2CPP + klient + overlay). Gotowy do gry.");

    public static string ModMono =>
        T("BepInEx 5 (Mono) is installed here. Star Trucker needs BepInEx 6 IL2CPP - press “Repair loader” to replace it.",
            "W folderze gry jest BepInEx 5 (Mono). Star Trucker wymaga BepInEx 6 IL2CPP - kliknij „Napraw loader”, aby go podmienić.");

    public static string ModNotInstalled =>
        T("Not installed - the launcher still starts Star Trucker normally. The multiplayer mod is optional.",
            "Niezainstalowany - launcher nadal normalnie uruchamia Star Trucker. Mod multiplayer jest opcjonalny.");

    public static string ModPartial(string missing) =>
        T($"Partial install - missing: {missing}. Press “Repair mod”.",
            $"Częściowa instalacja - brakuje: {missing}. Kliknij „Napraw mod”.");

    public static string MissingBepInEx => "BepInEx 6 IL2CPP";

    public static string MissingClient => T("client", "klient");

    public static string MissingOverlay => T("overlay", "overlay");

    // ----- BepInEx loader card -----

    public static string LoaderInstalled =>
        T("BepInEx 6 (IL2CPP) is in place. Plugins in BepInEx/plugins load normally.",
            "BepInEx 6 (IL2CPP) jest na miejscu. Pluginy z BepInEx/plugins wczytują się normalnie.");

    public static string LoaderMissing =>
        T("Not installed - Star Trucker still starts without mods. Install the loader even when the client package is not published yet.",
            "Niezainstalowany - Star Trucker nadal startuje bez modów. Zainstaluj loader nawet wtedy, gdy paczka klienta nie jest jeszcze opublikowana.");

    public static string LoaderMono =>
        T("BepInEx 5 (Mono) detected. Star Trucker needs BepInEx 6 IL2CPP - press “Install / repair BepInEx”.",
            "Wykryto BepInEx 5 (Mono). Star Trucker wymaga BepInEx 6 IL2CPP - kliknij „Zainstaluj / napraw BepInEx”.");

    public static string LoaderCaption =>
        T("Only the loader. The multiplayer client stays optional and can be added later.",
            "Sam loader. Klient multiplayera pozostaje opcjonalny i można go dodać później.");

    public static string LoaderInstalling => T("Installing the BepInEx loader...", "Instaluję loader BepInEx...");

    public static string LoaderInstallCompleted => T("BepInEx loader is ready.", "Loader BepInEx jest gotowy.");

    public static string LoaderInstallFailed(string message) =>
        T($"BepInEx installation failed: {message}", $"Instalacja BepInEx nie powiodła się: {message}");

    // ----- server editor -----

    public static string NoServerSelected => T("No server selected", "Nie wybrano serwera");

    public static string NewServerName => T("New server", "Nowy serwer");

    // ----- log / validation -----

    public static string GameNotFoundHint =>
        T("Star Trucker was not found. Use “Choose folder…” and pick the folder that contains Star Trucker.exe.",
            "Nie znaleziono Star Truckera. Użyj „Wybierz folder…” i wskaż katalog z plikiem Star Trucker.exe.");

    public static string GameNotDetected => T("Star Trucker was not detected.", "Nie wykryto Star Truckera.");

    public static string GameDetected(string path) =>
        T($"Using Star Trucker at {path}", $"Używam Star Truckera z {path}");

    public static string WarningPrefix => T("Warning: ", "Uwaga: ");

    public static string FolderHasNoGame =>
        T("That folder does not contain Star Trucker.exe.", "Ten folder nie zawiera Star Trucker.exe.");

    public static string AddServerFirst => T("Add a server before launching.", "Dodaj serwer przed uruchomieniem.");

    public static string ServerHasNoAddress => T("The selected server has no address.", "Wybrany serwer nie ma adresu.");

    public static string InvalidPort(string port) =>
        T($"'{port}' is not a valid port.", $"„{port}” nie jest poprawnym portem.");

    public static string ModMissingHint =>
        T("The multiplayer mod is not installed - starting Star Trucker without it. Press “Install / repair mod” to add it.",
            "Mod multiplayer nie jest zainstalowany - uruchamiam Star Trucker bez niego. Kliknij „Zainstaluj / napraw mod”, aby go dodać.");

    public static string ModInstalling => T("Installing mod…", "Instaluję mod…");

    public static string ModInstallCompleted => T("Mod installation completed.", "Instalacja moda zakończona.");

    public static string ModInstallFailed(string reason) =>
        T($"Mod installation failed: {reason}", $"Instalacja moda nie powiodła się: {reason}");

    public static string ServerSet(string address, string port) =>
        T($"Server set to {address}:{port}.", $"Ustawiono serwer {address}:{port}.");

    public static string LaunchedThroughSteam =>
        T("Asked Steam to start Star Trucker. The overlay appears once you load into the sector.",
            "Poproszono Steam o uruchomienie Star Truckera. Overlay pojawi się po wczytaniu sektora.");

    public static string LaunchedDirectly =>
        T("Started Star Trucker directly. Steam login may not work this way.",
            "Uruchomiono Star Truckera bezpośrednio. Logowanie Steam może nie działać.");

    public static string LaunchFailed(string reason) =>
        T($"Launch failed: {reason}", $"Uruchomienie nie powiodło się: {reason}");

    public static string ServerAdded => T("Added a new server entry.", "Dodano nowy serwer.");

    public static string KeepOneServer => T("At least one server entry has to stay.", "Musi zostać co najmniej jeden serwer.");

    public static string ServerRemoved(string name) =>
        T($"Removed server “{name}”.", $"Usunięto serwer „{name}”.");

    public static string AddressEmpty => T("The server address cannot be empty.", "Adres serwera nie może być pusty.");

    public static string PortRange =>
        T("The port has to be a number between 1 and 65535.", "Port musi być liczbą z zakresu 1–65535.");

    public static string ServerSaved(string name, string address, string port) =>
        T($"Saved server “{name}” ({address}:{port}).", $"Zapisano serwer „{name}” ({address}:{port}).");

    public static string NothingToOpen => T("Nothing to open yet.", "Nie ma jeszcze czego otworzyć.");

    public static string CouldNotOpen(string path, string reason) =>
        T($"Could not open {path}: {reason}", $"Nie udało się otworzyć {path}: {reason}");

    // ----- update check -----

    public static string UpToDate(string version) =>
        T($"Launcher v{version} is up to date.", $"Launcher v{version} jest aktualny.");

    public static string UpdateFound(string name, string tag, string url) =>
        T($"A newer launcher is available: {name} ({tag}). Download: {url}",
            $"Dostępny jest nowszy launcher: {name} ({tag}). Pobierz: {url}");

    public static string CardUpdate => T("NEW RELEASE", "NOWA WERSJA");

    public static string UpdateNow => T("Update now", "Zaktualizuj teraz");

    public static string ReleasePage => T("Release page", "Strona wydania");

    public static string UpdateNoNotes =>
        T("This release has no changelog.", "To wydanie nie ma listy zmian.");

    public static string UpdateCompareLabel => T("Changes:", "Zmiany:");

    public static string UpdateNotesCaption =>
        T("Changelog from the GitHub release.", "Lista zmian pochodzi z opisu wydania na GitHubie.");

    public static string UpdateHint =>
        T("Download and install this update now", "Pobierz i zainstaluj tę aktualizację teraz");

    public static string UpdateDownloading(string release) =>
        T($"Downloading {release}…", $"Pobieram {release}…");

    public static string UpdateReady(string release) =>
        T($"{release} is downloaded - the launcher restarts itself in a moment.",
            $"{release} jest pobrany - launcher uruchomi się ponownie za chwilę.");

    public static string UpdateFailed(string reason) =>
        T($"Update failed: {reason}", $"Aktualizacja nie powiodła się: {reason}");

    public static string UpdateAssetMissing =>
        T("This release has no launcher package to download.",
            "To wydanie nie ma paczki launchera do pobrania.");

    public static string UpdateHelperFailed =>
        T("Could not start the update helper.", "Nie udało się uruchomić programu aktualizującego.");

    public static string UpdateCheckSkipped(string reason) =>
        T($"Update check skipped: {reason}", $"Pominięto sprawdzanie aktualizacji: {reason}");

    public static string ReleasePageOpened(string url) =>
        T($"Opened the release page: {url}", $"Otwarto stronę wydania: {url}");

    public static string ReleasePageFailed(string reason) =>
        T($"Could not open the release page: {reason}", $"Nie udało się otworzyć strony wydania: {reason}");

    // ----- mod installation progress -----

    public static string BepInExAlreadyInstalled =>
        T("BepInEx 6 (IL2CPP) is already installed.", "BepInEx 6 (IL2CPP) jest już zainstalowany.");

    public static string BepInExMonoBackup(string backup) =>
        T($"BepInEx 5 (Mono) found - it cannot load this mod. Moving it to {backup}…",
            $"Wykryto BepInEx 5 (Mono) - nie obsługuje tego moda. Przenoszę go do {backup}…");

    public static string BepInExUsingPayload(string path) =>
        T($"Using the local BepInEx payload in {path}…", $"Używam lokalnego payloadu BepInEx z {path}…");

    public static string BepInExPayloadWriteFailed(string reason) =>
        T("The BepInEx payload could not be copied into the game folder. If the game is installed under Program Files, " +
          "run the launcher as administrator or move the Steam library to another drive. Details: " + reason,
            "Nie udało się skopiować payloadu BepInEx do folderu gry. Jeśli gra jest w Program Files, uruchom launcher " +
            "jako administrator albo przenieś bibliotekę Steam na inny dysk. Szczegóły: " + reason);

    public static string BepInExLookingUp =>
        T("Looking up the latest BepInEx 6 (IL2CPP, win-x64) build…", "Szukam najnowszego builda BepInEx 6 (IL2CPP, win-x64)…");

    public static string BepInExDownloading(string build) =>
        T($"Downloading BepInEx 6.0.0-be.{build}…", $"Pobieram BepInEx 6.0.0-be.{build}…");

    public static string BepInExExtracting =>
        T("Extracting BepInEx into the game folder…", "Rozpakowuję BepInEx do folderu gry…");

    public static string BepInExWriteFailed(string reason) =>
        T("BepInEx could not be written into the game folder. If the game is installed under Program Files, run the " +
          "launcher as administrator or move the Steam library to another drive. Details: " + reason,
            "Nie udało się zapisać BepInEx w folderze gry. Jeśli gra jest w Program Files, uruchom launcher jako " +
            "administrator albo przenieś bibliotekę Steam na inny dysk. Szczegóły: " + reason);

    public static string BepInExLayoutChanged =>
        T("BepInEx was extracted but the IL2CPP loader files are missing. The build layout may have changed.",
            "BepInEx został rozpakowany, ale brakuje plików loadera IL2CPP. Układ builda mógł się zmienić.");

    public static string BepInExReady =>
        T("BepInEx 6 (IL2CPP) is in place.", "BepInEx 6 (IL2CPP) jest na miejscu.");

    public static string BepInExNoBuild =>
        T("No BepInEx 6 IL2CPP win-x64 archive was found on builds.bepinex.dev.",
            "Nie znaleziono archiwum BepInEx 6 IL2CPP win-x64 na builds.bepinex.dev.");

    public static string GameRunningBlock =>
        T("Star Trucker is running. Close the game first - Windows keeps the mod files locked while it is open.",
            "Star Trucker jest uruchomiony. Najpierw zamknij grę - Windows blokuje pliki moda, gdy gra działa.");

    public static string ClientReady =>
        T("Multiplayer client is ready.", "Klient multiplayera jest gotowy.");

    public static string FirstLaunchNote =>
        T("The first launch after installing generates the BepInEx IL2CPP interop assemblies and can take a few minutes. " +
          "If the overlay does not appear the first time, restart the game once.",
            "Pierwsze uruchomienie po instalacji generuje biblioteki interop BepInEx IL2CPP i może potrwać kilka minut. " +
            "Jeśli overlay nie pojawi się za pierwszym razem, uruchom grę ponownie.");

    public static string ClientLookingUp =>
        T("Looking up the latest StarTruckerMP client release…", "Szukam najnowszego wydania klienta StarTruckerMP…");

    public static string ClientPackageMissing =>
        T("None of the Kaofan1/StarTruckerMP releases contains the mod package yet (StarTruckerMP-bepinex-plugin-win-x64.zip). " +
          "The launcher keeps working without it, but the multiplayer client has to be published in a release before the " +
          "launcher can add it - please report this in the Issues tab.",
            "Żadne wydanie Kaofan1/StarTruckerMP nie zawiera jeszcze paczki moda (StarTruckerMP-bepinex-plugin-win-x64.zip). " +
            "Launcher działa dalej bez niej, ale klient multiplayera musi zostać opublikowany w wydaniu, zanim launcher " +
            "będzie mógł go dodać - zgłoś to w zakładce Issues.");

    public static string ClientDownloading(string file) =>
        T($"Downloading {file}…", $"Pobieram {file}…");

    public static string ClientInstalling(string directory) =>
        T($"Installing the client into {directory}…", $"Instaluję klienta w {directory}…");

    public static string ClientWriteFailed(string reason) =>
        T("The client files could not be written into the game folder. Make sure the game is closed; if it is " +
          "installed under Program Files, run the launcher as administrator. Details: " + reason,
            "Nie udało się zapisać plików klienta w folderze gry. Upewnij się, że gra jest zamknięta; jeśli jest " +
            "w Program Files, uruchom launcher jako administrator. Szczegóły: " + reason);

    public static string ClientAssemblyMissing =>
        T("The client archive was extracted but StarTruckerMP.Client.dll is missing.",
            "Archiwum klienta zostało rozpakowane, ale brakuje StarTruckerMP.Client.dll.");

    public static string ClientWithoutOverlay =>
        T("Warning: the release does not contain the overlay. The radio UI will not be available.",
            "Uwaga: wydanie nie zawiera overlaya. Interfejs radia nie będzie dostępny.");

    public static string OverlayMissingFromArchive =>
        T("Warning: overlay/StarTruckerMP.Overlay.exe is missing from the client archive.",
            "Uwaga: w archiwum klienta brakuje overlay/StarTruckerMP.Overlay.exe.");

    public static string ClientInstallFinished =>
        T("Client installation finished.", "Instalacja klienta zakończona.");

    public static string SettingsMigrated =>
        T("Settings imported from the previous StarTruckMP folder.",
            "Zaimportowano ustawienia ze starego folderu StarTruckMP.");

    public static string LegacyPluginRemoved =>
        T("Removed the old StarTruckMP plugin folder left by the previous version.",
            "Usunięto stary folder pluginu StarTruckMP po poprzedniej wersji.");

    public static string ClientInstalledFrom(string url) =>
        T($"Client plugin installed from {url}", $"Plugin klienta zainstalowany z {url}");
}
