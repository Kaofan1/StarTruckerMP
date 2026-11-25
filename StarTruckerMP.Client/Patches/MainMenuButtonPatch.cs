#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using StarTruckerMP.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
    private const string ButtonName = "StarTruckerMP.MultiplayerButton";
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
            App.Log.LogWarning("[Menu] Multiplayer button was not added: " + exception.Message);
        }
    }

    private static void Install(MainMenuScreen? screen)
    {
        if (screen == null)
            return;

        if (App.MenuButtonEnabled is not null && !App.MenuButtonEnabled.Value)
            return;

        // Unity's operator, not "is null": a button the menu rebuilt is a destroyed object with
        // a live C# wrapper, and only == null recognises it.
        if (_button != null)
        {
            EnsureLabel(_button);
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
            EnsureLabel(_button);
            return;
        }

        // Cloning keeps the game's own look, font, sounds and animations.
        var clone = Object.Instantiate(source.gameObject, parent);
        clone.name = ButtonName;
        clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        clone.SetActive(true);

        // Localised components would overwrite the label with the original button's text.
        DisableLocalizers(clone);

        var menuButton = clone.GetComponent<MenuButton>();
        if (menuButton != null)
        {
            menuButton.m_OnClick.RemoveAllListeners();
            menuButton.m_OnClick.AddListener(
                DelegateSupport.ConvertDelegate<UnityAction>(new Action(MultiplayerPanel.Open)));
            menuButton.disableOnEnable = false;
            menuButton.SetEnabled(true);
        }

        EnsureLabel(clone);
        _button = clone;
        App.Log.LogInfo("[Menu] Multiplayer button added to the main menu.");
    }

    private static MenuButton? FirstButton(MainMenuScreen screen)
    {
        foreach (var candidate in new[]
                 {
                     screen.newGameButton, screen.continueButton, screen.scenarioButton,
                     screen.optionsButton, screen.exitButton
                 })
        {
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static void EnsureLabel(GameObject button)
    {
        var labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels is null || labels.Length == 0)
            return;

        var label = labels.FirstOrDefault(text => text != null && !string.IsNullOrWhiteSpace(text.text)) ?? labels[0];
        if (label != null)
            label.text = "Multiplayer";
    }

    private static void DisableLocalizers(GameObject button)
    {
        var behaviours = button.GetComponentsInChildren<MonoBehaviour>(true);
        if (behaviours is null)
            return;

        foreach (var behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            var typeName = behaviour.GetType().Name;
            if (typeName.Contains("Localiz", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("StringId", StringComparison.OrdinalIgnoreCase))
                behaviour.enabled = false;
        }
    }
}
