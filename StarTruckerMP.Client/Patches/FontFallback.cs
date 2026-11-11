using System;
using HarmonyLib;
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

    private static bool _installed;
    private static bool _gaveUp;
    private static int _attempts;

    public static bool Installed => _installed || _gaveUp;

    /// <summary>
    /// Attaches a fallback font to the affected assets. Safe to call repeatedly while the game is
    /// still loading its assets, and it stops on its own once it has either worked or run out of
    /// attempts.
    /// </summary>
    public static void TryInstall()
    {
        if (_installed || _gaveUp)
            return;

        if (++_attempts > MaxAttempts)
        {
            _gaveUp = true;
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
            }

            patched++;
        }

        if (patched > 0)
            _installed = true;
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

/// <summary>
/// TextMeshPro warns about every missing glyph, and the affected texts are refreshed constantly,
/// so the two known font asset messages are dropped before they reach the log. Everything else is
/// passed through untouched.
/// </summary>
[HarmonyPatch]
internal static class FontWarningFilter
{
    [HarmonyPatch(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) })]
    [HarmonyPrefix]
    private static bool Prefix(object message)
    {
        var text = message?.ToString();
        if (string.IsNullOrEmpty(text))
            return true;

        if (text.Contains("is not available in font asset", StringComparison.Ordinal))
            return false;

        if (text.Contains("was not found in the", StringComparison.Ordinal) &&
            text.Contains("SDF]", StringComparison.Ordinal))
            return false;

        return true;
    }
}
