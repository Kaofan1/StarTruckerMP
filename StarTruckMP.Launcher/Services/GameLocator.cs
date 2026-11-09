namespace StarTruckMP.Launcher.Services;

internal static class GameLocator
{
    /// <summary>
    /// Returns the game install for <paramref name="preferredPath"/> when it is valid,
    /// otherwise falls back to Steam / common library detection.
    /// </summary>
    public static GameInstall? Locate(string? preferredPath)
    {
        var preferred = GameInstall.FromPath(preferredPath);
        if (preferred is not null)
            return preferred;

        var detected = SteamLibraryLocator.FindStarTruckerInstall();
        return GameInstall.FromPath(detected);
    }
}
