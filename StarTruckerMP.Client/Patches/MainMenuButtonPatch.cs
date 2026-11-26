#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StarTruckerMP.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StarTruckerMP.Client.Patches;

/// <summary>
/// Adds a "Multiplayer" button to the game's main menu. Everything here is best effort: a game
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
    private static readonly string LabelText = "Multiplayer";
    private static GameObject? _button;

    // The menu is shifted down one row so this button can sit on top without covering the title.
    // Remembering the original heights keeps repeated refreshes from moving the rows twice.
    private static readonly Dictionary<int, float> OriginalY = new();

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

            var currentParent = _button.transform.parent;
            if (currentParent != null && currentParent.GetComponent<LayoutGroup>() != null)
                PlaceAboveEntries(screen, _button.transform);
            else
                EnsureLayout(screen, _button.GetComponent<RectTransform>());

            return;
        }

        var source = TopButton(screen);
        if (source == null)
            return;

        var parent = source.transform.parent;
        var sourceRect = source.GetComponent<RectTransform>();
        if (parent == null || sourceRect == null)
            return;

        var existing = parent.Find(ButtonName);
        if (existing != null)
        {
            _button = existing.gameObject;
            EnsureLayout(screen, existing.GetComponent<RectTransform>());
            return;
        }

        // The clone brings the game's own look: label, hover and press animations, sounds.
        var clone = Object.Instantiate(source.gameObject, parent, false);
        clone.name = ButtonName;

        // A layout group owns the heights, so the order of the children decides the position:
        // taking the first entry's slot puts this button directly above it.
        var layout = parent.GetComponent<LayoutGroup>();
        if (layout != null)
            PlaceAboveEntries(screen, clone.transform);

        // The menu label localises itself from the string table; removing that keeps "Multiplayer".
        foreach (var lookup in clone.GetComponentsInChildren<StringTableLookup>(true))
            Object.DestroyImmediate(lookup);

        foreach (var label in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.text = LabelText;
            label.raycastTarget = false;
        }

        // The prefab stores the game action as a serialized listener, which RemoveAllListeners
        // does not clear. Replacing the event object drops it and leaves only our own action.
        var action = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(
            new Action(MultiplayerPanel.Open));

        var menuButton = clone.GetComponent<MenuButton>();
        if (menuButton != null)
        {
            menuButton.disableOnEnable = false;
            menuButton.m_OnClick = new Button.ButtonClickedEvent();
            menuButton.m_OnClick.AddListener(action);
        }

        var button = clone.GetComponent<Button>();
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            button.interactable = true;
        }

        if (menuButton == null && button == null)
        {
            var added = clone.AddComponent<Button>();
            added.onClick = new Button.ButtonClickedEvent();
            added.onClick.AddListener(action);
        }

        // A disabled "Continue" entry would clone as non-interactable.
        menuButton?.SetEnabled(true);
        clone.SetActive(true);

        // A copy can inherit the entry's selected look; the menu is what selects an entry.
        if (menuButton != null)
        {
            menuButton.isSelected = false;
            menuButton.isHoverHighlighted = false;
            menuButton.isPressed = false;
            menuButton.ForceRefresh();
        }

        var eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == clone)
        {
            var fallback = screen.defaultButton != null ? screen.defaultButton.gameObject : null;
            eventSystem.SetSelectedGameObject(fallback);
        }

        _button = clone;
        PlaceAboveEntries(screen, clone.transform);
        if (layout == null)
            EnsureLayout(screen, clone.GetComponent<RectTransform>());

        App.Log.LogInfo("[Menu] Multiplayer button added above the first menu entry.");
    }

    /// <summary>
    /// The layout group places children in sibling order. Sitting before every menu entry keeps
    /// this button on top even when the game enables "Continue" later, and keeps the title above.
    /// </summary>
    private static void PlaceAboveEntries(MainMenuScreen screen, Transform button)
    {
        var parent = button.parent;
        if (parent == null)
            return;

        var first = int.MaxValue;
        foreach (var candidate in MenuButtons(screen))
        {
            if (candidate == null)
                continue;

            var transform = candidate.transform;
            if (transform.parent == parent)
                first = Mathf.Min(first, transform.GetSiblingIndex());
        }

        // Already above every entry: moving again would push the button below "Continue".
        if (first == int.MaxValue || button.GetSiblingIndex() <= first)
            return;

        button.SetSiblingIndex(first);
    }

    /// <summary>
    /// Fallback for a menu without a layout group: keeps this button on top and shifts the
    /// game's rows one step down, once.
    /// </summary>
    private static void EnsureLayout(MainMenuScreen screen, RectTransform? buttonRect)
    {
        if (buttonRect == null)
            return;

        var step = RowStep(screen);
        var top = float.MinValue;
        var rows = new List<(RectTransform Rect, float Original)>();

        foreach (var candidate in MenuButtons(screen))
        {
            if (candidate == null)
                continue;

            var rect = candidate.GetComponent<RectTransform>();
            if (rect == null)
                continue;

            var id = rect.GetInstanceID();
            if (!OriginalY.TryGetValue(id, out var original))
            {
                original = rect.anchoredPosition.y;
                OriginalY[id] = original;
            }

            top = Mathf.Max(top, original);
            rows.Add((rect, original));
        }

        if (rows.Count == 0 || top == float.MinValue)
            return;

        foreach (var (rect, original) in rows)
            Place(rect, original - step);

        Place(buttonRect, top);
    }

    private static void Place(RectTransform rect, float y)
    {
        var position = rect.anchoredPosition;
        if (Mathf.Approximately(position.y, y))
            return;

        rect.anchoredPosition = new Vector2(position.x, y);
    }

    private static MenuButton? TopButton(MainMenuScreen screen)
    {
        MenuButton? top = null;
        var topY = float.MinValue;

        foreach (var candidate in MenuButtons(screen))
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
                continue;

            var rect = candidate.GetComponent<RectTransform>();
            if (rect == null)
                continue;

            var id = rect.GetInstanceID();
            var y = OriginalY.TryGetValue(id, out var original) ? original : rect.anchoredPosition.y;
            if (y > topY)
            {
                topY = y;
                top = candidate;
            }
        }

        return top;
    }

    private static MenuButton?[] MenuButtons(MainMenuScreen screen) => new[]
    {
        screen.newGameButton, screen.continueButton, screen.loadButton, screen.scenarioButton,
        screen.optionsButton, screen.creditsButton, screen.dlcButton, screen.exitButton
    };

    private static float RowStep(MainMenuScreen screen)
    {
        var active = MenuButtons(screen)
            .Where(candidate => candidate != null && candidate.gameObject.activeInHierarchy)
            .Select(candidate => candidate!.GetComponent<RectTransform>())
            .Where(rect => rect != null)
            .Select(rect => rect!.anchoredPosition.y)
            .Distinct()
            .OrderByDescending(y => y)
            .Take(2)
            .ToArray();

        if (active.Length >= 2)
        {
            var step = Mathf.Abs(active[0] - active[1]);
            if (step > 1f)
                return step;
        }

        return 60f;
    }
}
