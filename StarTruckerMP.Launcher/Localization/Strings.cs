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

    public static string InstallMod => T("Install / repair mod", "Zainstaluj / napraw mod");

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
        T("BepInEx 5 (Mono) is installed here. Star Trucker needs BepInEx 6 IL2CPP - press “Install / repair mod” to replace it.",
            "W folderze gry jest BepInEx 5 (Mono). Star Trucker wymaga BepInEx 6 IL2CPP - kliknij „Zainstaluj / napraw mod”, aby go podmienić.");

    public static string ModNotInstalled =>
        T("Not installed - the launcher still starts Star Trucker normally. The multiplayer mod is optional.",
            "Niezainstalowany - launcher nadal normalnie uruchamia Star Trucker. Mod multiplayer jest opcjonalny.");

    public static string ModPartial(string missing) =>
        T($"Partial install - missing: {missing}. Press “Install / repair mod”.",
            $"Częściowa instalacja - brakuje: {missing}. Kliknij „Zainstaluj / napraw mod”.");

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

    public static string InstallLoader => T("Install / repair BepInEx", "Zainstaluj / napraw BepInEx");

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
