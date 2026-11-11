using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// The game ships two dot matrix fonts without the Polish L with stroke and without the underline
/// glyph, so TextMeshPro draws a space there. Another font asset of the game that does have the
/// Polish glyphs is attached to both as a fallback.
/// </summary>
public static class FontFallback
{
    private static readonly string[] AffectedFonts =
    [
        "DigitalDot-Regular SDF",
        "Digital7-Mono-Regular_SDF"
    ];

    private const char PolishL = '\u0141';
    private const char Underline = '\u0332';
    private const int MaxAttempts = 40;

    private static bool _finished;
    private static int _attempts;

    public static bool Installed => _finished;

    /// <summary>
    /// Attaches a fallback font to the affected assets. Safe to call repeatedly while the game is
    /// still loading its assets, and it stops on its own once the attempts run out.
    /// </summary>
    public static void TryInstall()
    {
        if (_finished)
            return;

        if (++_attempts > MaxAttempts)
        {
            _finished = true;
            App.Log.LogInfo("[Fonts] No loaded font carries the Polish glyph, keeping the warnings filtered.");
            return;
        }

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (fonts == null || fonts.Length == 0)
            return;

        var fallback = FindFallback(fonts);
        if (fallback == null)
            return;

        var patched = 0;

        foreach (var font in fonts)
        {
            if (font == null || string.IsNullOrEmpty(font.name))
                continue;

            if (Array.IndexOf(AffectedFonts, font.name) < 0)
                continue;

            if (font.HasCharacter(PolishL) && font.HasCharacter(Underline))
                continue;

            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();

            if (!font.fallbackFontAssetTable.Contains(fallback))
            {
                font.fallbackFontAssetTable.Add(fallback);
                App.Log.LogInfo($"[Fonts] Using {fallback.name} as the fallback for {font.name}.");
                patched++;
            }
        }

        // The two fonts do not necessarily load in the same frame, so the attempts continue until
        // the cap is reached and every asset that shows up gets the fallback.
        if (patched > 0)
            App.Log.LogInfo($"[Fonts] Fallback applied to {patched} font asset(s) in this pass.");
    }

    private static TMP_FontAsset FindFallback(Il2CppArrayBase<TMP_FontAsset> fonts)
    {
        TMP_FontAsset best = null;

        foreach (var font in fonts)
        {
            if (font == null || string.IsNullOrEmpty(font.name))
                continue;

            if (Array.IndexOf(AffectedFonts, font.name) >= 0)
                continue;

            if (!font.HasCharacter(PolishL))
                continue;

            // A font that also has the underline glyph covers both warnings at once.
            if (font.HasCharacter(Underline))
                return font;

            best ??= font;
        }

        return best;
    }
}
