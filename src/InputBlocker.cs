using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QuestTracker;

/// <summary>
/// While our quest list or layout mode is open, the game must not react to the mouse or keys:
/// Rewired players are paused (no moving, no aiming) and the UI input module is switched off
/// (no clicking through onto buttons behind our window). Everything is restored when it closes.
/// Only local input is touched, so co-op is unaffected.
/// </summary>
internal static class InputBlocker
{
    public static bool Blocking { get; private set; }

    private static readonly List<Player> PausedPlayers = new();
    private static BaseInputModule _module;
    private static bool _moduleWasEnabled;

    public static void Update(bool block)
    {
        if (block == Blocking) return;
        Blocking = block;
        if (block) Block(); else Unblock();
    }

    private static void Block()
    {
        try
        {
            if (ReInput.isReady)
            {
                var players = ReInput.players;
                for (int i = 0; i < players.playerCount; i++)
                {
                    var p = players.GetPlayer(i);
                    if (p == null || !p.isPlaying) continue;
                    p.isPlaying = false;
                    PausedPlayers.Add(p);
                }
            }
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Input block (Rewired): {e.Message}"); }

        try
        {
            // Only the part that reads mouse and keys. Disabling the whole EventSystem makes the game's
            // UiFocusManager log an error every frame (it expects EventSystem.current to exist).
            _module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
            if (_module != null)
            {
                _moduleWasEnabled = _module.enabled;
                _module.enabled = false;
            }
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Input block (UI): {e.Message}"); }
    }

    private static void Unblock()
    {
        foreach (var p in PausedPlayers)
        {
            try { p.isPlaying = true; }
            catch { /* player gone */ }
        }
        PausedPlayers.Clear();

        try { if (_module != null && !_module.WasCollected) _module.enabled = _moduleWasEnabled; }
        catch { /* input module gone */ }
        _module = null;
    }
}
