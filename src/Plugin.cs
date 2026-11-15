using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace QuestTracker;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "questtracker.megabonk";
    public const string Name = "Quest Tracker";
    public const string Version = "1.3.2";

    internal static ManualLogSource Logger;
    internal static Settings Cfg;
    internal static UpdateChecker Updates;

    public override void Load()
    {
        Logger = Log;
        Cfg = new Settings(Config);
        Tracker.LoadPins();

        AddComponent<TrackerBehaviour>();

        if (Cfg.CheckForUpdates.Value)
        {
            Updates = new UpdateChecker(Name, 192, Version, 0, () => L.Arabic, s => Rtl.Visual(s, GUI.skin.label, 0f));
            Updates.Start(msg => Log.LogInfo(msg));
        }

        Log.LogInfo($"{Name} {Version} loaded. Hover a quest and press {Cfg.PinKey.Value} to track it, or press {Cfg.PickerKey.Value} to open the quest list.");
    }
}

/// <summary>Drives the tracker from Unity's update loop. Read-only: it never changes game state.</summary>
public class TrackerBehaviour : MonoBehaviour
{
    public TrackerBehaviour(IntPtr ptr) : base(ptr) { }

    private float _nextTick;

    public void Update()
    {
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.2f;
        Tracker.Tick();
    }

    public void OnGUI()
    {
        Hud.Draw();
        try { Plugin.Updates?.OnGUI(!Tracker.InRun); }
        catch { /* the notice must never get in the way */ }
    }
}
