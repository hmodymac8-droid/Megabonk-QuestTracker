using System;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QuestTracker;

/// <summary>
/// Finds the quest under the mouse in the game's menus by asking the game's own UI event system what is
/// hovered. This replaces Harmony hooks on button hover/click methods: the game's compiler merges identical
/// functions, so those hooks could fire from unrelated code with bad data and crash the game.
/// Handles: Quests menu buttons (key only, since clicking claims rewards), main-menu quick quests,
/// locked skins and locked Unlocks-shop entries (click or key).
/// </summary>
internal static class UiHoverScanner
{
    private static bool _hovering;
    private static MyAchievement _clickable; // what a left-click would track right now
    private static Il2CppSystem.Collections.Generic.List<RaycastResult> _results;
    private static PointerEventData _pointer;
    private static EventSystem _pointerOwner;
    private const float DoubleClickSeconds = 0.4f;
    private static MyAchievement _lastClick;
    private static float _lastClickTime;

    public static void Scan(Event e)
    {
        if (e == null || (e.type != EventType.Repaint && e.type != EventType.MouseDown)) return;

        try
        {
            if (Tracker.InRun || Picker.Open || Hud.LayoutMode) { Clear(); return; }

            // A double-click tracks whatever was hovered on the last repaint; a single click is left to the game.
            if (e.type == EventType.MouseDown)
            {
                if (e.button != 0 || _clickable == null) return;
                bool second = _lastClick != null && _lastClick.Pointer == _clickable.Pointer && Time.unscaledTime - _lastClickTime < DoubleClickSeconds;
                if (second) { Tracker.ClickTrack(_clickable); _lastClick = null; }
                else { _lastClick = _clickable; _lastClickTime = Time.unscaledTime; }
                return;
            }

            var es = EventSystem.current;
            if (es == null || !es.isActiveAndEnabled) { Clear(); return; }

            if (_pointer == null || _pointerOwner == null || _pointerOwner.Pointer != es.Pointer)
            {
                _pointer = new PointerEventData(es);
                _pointerOwner = es;
            }
            _results ??= new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            _results.Clear();
            _pointer.position = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
            es.RaycastAll(_pointer, _results);

            for (int i = 0; i < _results.Count; i++)
            {
                var go = _results[i].gameObject;
                if (go == null) continue;
                if (TryHover(go)) return;
            }
            Clear();
        }
        catch (Exception ex)
        {
            Clear();
            Plugin.Logger.LogDebug($"UI hover scan: {ex.Message}");
        }
    }

    private static bool TryHover(GameObject go)
    {
        var quest = go.GetComponentInParent<MyButtonQuest>();
        if (quest != null && quest.achievement != null)
        {
            Set(quest.achievement, HoverKind.KeyOnly, clickable: false);
            return true;
        }

        var quick = go.GetComponentInParent<QuickQuestContainer>();
        if (quick != null && quick.currentAchievement != null)
        {
            Set(quick.currentAchievement, HoverKind.Clickable, clickable: true);
            return true;
        }

        var skin = go.GetComponentInParent<SkinContainer>();
        if (skin != null)
        {
            if (skin.skin == null || !skin.IsLocked()) return false;
            var req = skin.skin.GetUnlockRequirement();
            if (req == null) return false;
            Set(req, HoverKind.Clickable, clickable: true);
            return true;
        }

        var unlock = go.GetComponentInParent<MyButtonUnlock>();
        var container = unlock != null ? unlock.unlockContainer : null;
        if (container != null && !container.isUnlocked && container.unlockable != null)
        {
            var req = container.unlockable.GetUnlockRequirement();
            if (req == null) return false;
            Set(req, HoverKind.Clickable, clickable: true);
            return true;
        }
        return false;
    }

    private static void Set(MyAchievement a, HoverKind kind, bool clickable)
    {
        Tracker.SetHovered(a, kind);
        _hovering = true;
        _clickable = clickable && Plugin.Cfg.ClickToTrack.Value ? a : null;
    }

    private static void Clear()
    {
        _clickable = null;
        if (!_hovering) return;
        _hovering = false;
        Tracker.ClearHovered(null);
    }
}
