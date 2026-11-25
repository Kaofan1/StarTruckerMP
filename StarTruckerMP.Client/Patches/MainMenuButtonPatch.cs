#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using StarTruckerMP.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// Adds a "Multiplayer" entry to the game's main menu. Everything here is best effort: a game
/// update may rename or restructure the menu, and in that case the button is simply not added
/// instead of taking the plugin or the game down. F9 opens the same panel as a fallback.
/// </summary>
/// <remarks>
/// The two patches live in separate classes on purpose: Harmony aborts a patch class when one of
/// its target methods is missing, so a renamed refresh method must not also lose the menu button.
/// </remarks>
[HarmonyPatch(typeof(MainMenuScreen), "Start")]
internal static class MainMenuButtonPatch
{
    private static void Postfix(MainMenuScreen __instance) => MainMenuButtons.InstallSafely(__instance);
}

[HarmonyPatch(typeof(MainMenuScreen), "RefreshControlVisibility")]
internal static class MainMenuButtonRefreshPatch
{
    private static void Postfix(MainMenuScreen __instance) => MainMenuButtons.InstallSafely(__instance);
}

internal static class MainMenuButtons
{
    internal const string ButtonName = "StarTruckerMP.MultiplayerButton";
    private static GameObject? _button;

    public static void InstallSafely(MainMenuScreen? screen)
    {
        try
        {
            Install(screen);
        }
        catch (Exception exception)
        {
            // A menu change must never crash the game; the log keeps it diagnosable.
            App.Log.LogWarning("[Menu] Multiplayer button was not added: " + exception);
        }
    }

    private static void Install(MainMenuScreen? screen)
    {
        if (screen == null)
            return;

        if (App.MenuButtonEnabled is not null && !App.MenuButtonEnabled.Value)
            return;

        // Unity's operator, not "is null": a row the menu rebuilt is a destroyed object with a
        // live C# wrapper, and only == null recognises it.
        if (_button != null)
        {
            if (!_button.activeSelf)
                _button.SetActive(true);

            return;
        }

        var source = FirstButton(screen);
        if (source == null)
            return;

        var parent = source.transform.parent;
        if (parent == null)
            return;

        var existing = parent.Find(ButtonName);
        if (existing != null)
        {
            _button = existing.gameObject;
            return;
        }

        var root = new GameObject(ButtonName);
        root.transform.SetParent(parent, false);
        root.layer = source.gameObject.layer;

        var sourceRect = source.GetComponent<RectTransform>();
        var rect = root.AddComponent<RectTransform>();
        if (sourceRect != null)
        {
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.sizeDelta = new Vector2(420f, 40f);
            rect.anchoredPosition = new Vector2(
                sourceRect.anchoredPosition.x,
                LowestY(screen) - Mathf.Abs(RowStep(screen)));
        }

        var image = root.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.002f);
        image.raycastTarget = true;

        // Cloning the label keeps the game's font, size and spacing.
        var labelSource = FindLabel(source);
        if (labelSource == null)
            return;

        var labelObject = Object.Instantiate(labelSource.gameObject, root.transform);
        labelObject.name = "Label";
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = "Multiplayer";
            label.color = Color.white;
            label.raycastTarget = false;
            label.fontSize = labelSource.fontSize;
            label.enableAutoSizing = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        var click = root.GetComponent<Button>();
        if (click == null)
            click = root.AddComponent<Button>();

        if (click == null)
            return;

        if (click.onClick == null)
            click.onClick = new Button.ButtonClickedEvent();

        click.onClick.AddListener(
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(
                new Action(MultiplayerPanel.Open)));

        _button = root;
        App.Log.LogInfo("[Menu] Multiplayer button added to the main menu.");
    }

    private static MenuButton? FirstButton(MainMenuScreen screen)
    {
        foreach (var candidate in MenuButtons(screen))
        {
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static MenuButton?[] MenuButtons(MainMenuScreen screen) => new[]
    {
        screen.newGameButton, screen.continueButton, screen.loadButton, screen.scenarioButton,
        screen.optionsButton, screen.creditsButton, screen.dlcButton, screen.exitButton
    };

    private static float LowestY(MainMenuScreen screen)
    {
        var lowest = float.MaxValue;
        foreach (var candidate in MenuButtons(screen))
        {
            if (candidate == null)
                continue;

            var rect = candidate.GetComponent<RectTransform>();
            if (rect != null)
                lowest = Mathf.Min(lowest, rect.anchoredPosition.y);
        }

        return lowest == float.MaxValue ? -180f : lowest;
    }

    private static float RowStep(MainMenuScreen screen)
    {
        var first = screen.continueButton != null
            ? screen.continueButton.GetComponent<RectTransform>()
            : null;
        var second = screen.newGameButton != null
            ? screen.newGameButton.GetComponent<RectTransform>()
            : null;

        if (first != null && second != null)
        {
            var step = Mathf.Abs(first.anchoredPosition.y - second.anchoredPosition.y);
            if (step > 1f)
                return step;
        }

        return 60f;
    }

    private static TextMeshProUGUI? FindLabel(MenuButton source)
    {
        var labels = source.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels == null || labels.Length == 0)
            return null;

        return labels.FirstOrDefault(text => text != null && !string.IsNullOrWhiteSpace(text.text))
               ?? labels.FirstOrDefault(text => text != null);
    }
}
