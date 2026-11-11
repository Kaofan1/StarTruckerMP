using StarTruckerMP.Client.Components;
using StarTruckerMP.Client.Synchronization;
using TMPro;
using UnityEngine;

namespace StarTruckerMP.Client.UI;

/// <summary>
/// Builds the world-space labels that show a remote player's name and network id above their suit
/// and above their truck.
/// </summary>
internal static class Nameplates
{
    // Fallbacks only: the real height is measured from the rendered model so the label sits above
    // the roof instead of inside the cab.
    private const float SuitFallbackHeight = 1.9f;
    private const float TruckFallbackHeight = 3.5f;

    private const float SuitMargin = 0.35f;
    private const float TruckMargin = 0.8f;

    private const float SuitFullDistance = 25f;
    private const float SuitHiddenDistance = 120f;

    private const float TruckFullDistance = 60f;
    private const float TruckHiddenDistance = 400f;

    private const float SuitLabelWorldHeight = 0.45f;
    private const float TruckLabelWorldHeight = 1.4f;

    private static TMP_FontAsset _font;
    private static bool _fontSearched;

    public static GameObject CreateSuitLabel(Transform target, string text) =>
        Create(target, text, SuitFallbackHeight, SuitMargin, SuitFullDistance, SuitHiddenDistance, SuitLabelWorldHeight);

    public static GameObject CreateTruckLabel(Transform target, string text) =>
        Create(target, text, TruckFallbackHeight, TruckMargin, TruckFullDistance, TruckHiddenDistance, TruckLabelWorldHeight);

    public static void SetText(GameObject label, string text)
    {
        if (label == null)
            return;

        var component = label.GetComponent<TMP_Text>();
        if (component == null)
            return;

        component.text = text;
    }

    public static void SetTarget(GameObject label, Transform target)
    {
        if (label == null || target == null)
            return;

        var component = label.GetComponent<NameplateComponent>();
        if (component == null)
            return;

        component.SetTarget(target);
    }

    public static void SetVisible(GameObject label, bool visible)
    {
        if (label == null)
            return;

        var component = label.GetComponent<NameplateComponent>();
        if (component == null)
            return;

        component.SetSuspended(!visible);
    }

    private static GameObject Create(Transform target, string text, float fallbackHeight, float margin, float fullDistance, float hiddenDistance, float worldHeight)
    {
        if (target == null)
            return null;

        var font = ResolveFont();
        if (font == null)
            return null;

        var labelObject = new GameObject($"Nameplate-{text}");
        labelObject.transform.position = target.position + Vector3.up * fallbackHeight;

        var label = labelObject.AddComponent<TextMeshPro>();
        label.font = font;
        label.text = text;
        label.fontSize = 48f;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(40f, 8f);

        var nameplate = labelObject.AddComponent<NameplateComponent>();
        nameplate.Configure(target, fallbackHeight, margin, fullDistance, hiddenDistance, worldHeight);

        return labelObject;
    }

    private static TMP_FontAsset ResolveFont()
    {
        if (_fontSearched)
            return _font;

        _fontSearched = true;

        try
        {
            _font = TMP_Settings.defaultFontAsset;
        }
        catch
        {
            // TMP settings are missing from the build; the fallbacks below still apply.
        }

        if (_font == null)
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        if (_font == null)
        {
            // Any label already in the game carries a usable font asset.
            var existing = UnityEngine.Object.FindObjectOfType<TMP_Text>();
            if (existing != null)
                _font = existing.font;
        }

        if (_font == null)
            App.Log.LogWarning("[Nameplates] No TextMeshPro font asset found, remote names are disabled.");
        else
            App.Log.LogInfo($"[Nameplates] Using font '{_font.name}'.");

        return _font;
    }
}
