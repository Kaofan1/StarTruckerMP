using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// The game ships two digital fonts without the Polish L with stroke and without the underline
/// glyph, so TextMeshPro draws a space and logs a warning every time such a text is refreshed.
/// A dynamic fallback font built from a system font fills both gaps.
/// </summary>
public static class FontFallback
{
    private static readonly string[] AffectedFonts =
    [
        "DigitalDot-Regular SDF",
        "Digital7-Mono-Regular_SDF"
    ];

    private static TMP_FontAsset _fallback;
    private static bool _installed;

    public static bool Installed => _installed;

    /// <summary>
    /// Adds the fallback to every affected font asset that is loaded. Safe to call repeatedly while
    /// the game is still loading its assets.
    /// </summary>
    public static void TryInstall()
    {
        if (_installed)
            return;

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var matched = 0;

        foreach (var font in fonts)
        {
            if (font == null || string.IsNullOrEmpty(font.name))
                continue;

            if (Array.IndexOf(AffectedFonts, font.name) < 0)
                continue;

            if (font.HasCharacter('\u0141') && font.HasCharacter('\u0332'))
                continue;

            EnsureFallback();
            if (_fallback == null)
                return;

            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();

            if (!font.fallbackFontAssetTable.Contains(_fallback))
            {
                font.fallbackFontAssetTable.Add(_fallback);
                App.Log.LogInfo($"[Fonts] Added the fallback font to {font.name}.");
            }

            matched++;
        }

        if (matched > 0)
            _installed = true;
    }

    private static void EnsureFallback()
    {
        if (_fallback != null)
            return;

        try
        {
            var osFont = Font.CreateDynamicFontFromOSFont("Arial", 48);
            if (osFont == null)
            {
                App.Log.LogWarning("[Fonts] Arial is not available, cannot build a fallback font.");
                return;
            }

            _fallback = TMP_FontAsset.CreateFontAsset(
                osFont,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (_fallback == null)
            {
                App.Log.LogWarning("[Fonts] TextMeshPro refused to create the fallback font.");
                return;
            }

            _fallback.name = "StarTruckerMP Fallback";
            App.Log.LogInfo("[Fonts] Fallback font created.");
        }
        catch (Exception exception)
        {
            App.Log.LogWarning("[Fonts] Could not create a fallback font: " + exception.Message);
        }
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
