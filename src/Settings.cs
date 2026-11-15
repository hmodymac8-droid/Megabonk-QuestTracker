using BepInEx.Configuration;
using UnityEngine;

namespace QuestTracker;

internal class Settings
{
    public const float DefaultPosX = 0.012f;
    public const float DefaultPosY = 0.36f;
    public const float DefaultScale = 1f;

    public readonly ConfigEntry<KeyCode> PinKey;
    public readonly ConfigEntry<KeyCode> PickerKey;
    public readonly ConfigEntry<KeyCode> HudKey;
    public readonly ConfigEntry<KeyCode> LayoutKey;
    public readonly ConfigEntry<KeyCode> DumpKey;
    public readonly ConfigEntry<KeyCode> GuideKey;

    public readonly ConfigEntry<float> PosX;
    public readonly ConfigEntry<float> PosY;
    public readonly ConfigEntry<float> Scale;
    public readonly ConfigEntry<float> Opacity;
    public readonly ConfigEntry<bool> ShowHud;
    public readonly ConfigEntry<bool> ShowInMenus;
    public readonly ConfigEntry<bool> ClickToTrack;
    public readonly ConfigEntry<float> HideFailedAfter;
    public readonly ConfigEntry<float> HideCompletedAfter;

    public readonly ConfigEntry<bool> ShowGuide;
    public readonly ConfigEntry<bool> GuideMinimap;
    public readonly ConfigEntry<float> GuideScale;

    public readonly ConfigEntry<string> Language;
    public readonly ConfigEntry<bool> CheckForUpdates;
    public readonly ConfigEntry<int> MaxPinned;
    public readonly ConfigEntry<string> Pinned;
    public readonly ConfigEntry<string> TimerOverrides;

    private readonly ConfigFile _file;

    /// <summary>Writes the config file. Auto-save is off so dragging the panel doesn't write the file every frame.</summary>
    public void Save() => _file.Save();

    public Settings(ConfigFile cfg)
    {
        _file = cfg;
        PinKey = cfg.Bind("Keys", "TrackHoveredQuest", KeyCode.T,
            "Hover a quest (Quests menu, main-menu quests, a locked skin or a locked unlock) and press this key to track or untrack it.");
        PickerKey = cfg.Bind("Keys", "OpenQuestList", KeyCode.F7,
            "Opens the quest list so you can pick quests to track from anywhere.");
        HudKey = cfg.Bind("Keys", "ToggleTracker", KeyCode.F6,
            "Shows or hides the tracker panel.");
        LayoutKey = cfg.Bind("Keys", "MoveResizeTracker", KeyCode.F8,
            "Layout mode: drag the panel to move it, scroll or drag its corner to resize. Press again (or Esc) when done.");
        DumpKey = cfg.Bind("Keys", "ExportQuestList", KeyCode.F9,
            "Writes every quest and its progress to BepInEx/QuestTracker_quests.tsv (handy for bug reports).");
        GuideKey = cfg.Bind("Keys", "ToggleQuestGuide", KeyCode.F10,
            "Shows or hides the quest guide (the arrow around your character that shows which way to go for a tracked quest).");

        PosX = cfg.Bind("Panel", "PositionX", DefaultPosX, new ConfigDescription("Left edge of the panel, as a share of screen width (0 = left, 1 = right). Easier: press F8 in game and drag it.", new AcceptableValueRange<float>(0f, 1f)));
        PosY = cfg.Bind("Panel", "PositionY", DefaultPosY, new ConfigDescription("Top edge of the panel, as a share of screen height (0 = top, 1 = bottom).", new AcceptableValueRange<float>(0f, 1f)));
        Scale = cfg.Bind("Panel", "Size", DefaultScale, new ConfigDescription("Size of the panel (1 = default). It also scales with your screen resolution.", new AcceptableValueRange<float>(0.5f, 2.5f)));
        Opacity = cfg.Bind("Panel", "BackgroundOpacity", 0.8f, new ConfigDescription("How dark the panel background is.", new AcceptableValueRange<float>(0f, 1f)));
        ShowHud = cfg.Bind("Panel", "Visible", true, "Whether the tracker panel is shown (also toggled in game with the ToggleTracker key).");
        ShowInMenus = cfg.Bind("Panel", "ShowInMenus", true, "Also show tracked quests while in the main menu.");
        ClickToTrack = cfg.Bind("Panel", "ClickToTrack", true, "Double-clicking a main-menu quest, a locked skin or a locked unlock also tracks it.");
        HideFailedAfter = cfg.Bind("Panel", "HideFailedAfterSeconds", 8f, new ConfigDescription(
            "A failed quest disappears from the panel after this many seconds (it stays tracked and comes back on the next stage or run). 0 = never hide.",
            new AcceptableValueRange<float>(0f, 120f)));
        HideCompletedAfter = cfg.Bind("Panel", "HideCompletedAfterSeconds", 8f, new ConfigDescription(
            "A completed quest disappears from the panel after this many seconds (remove it for good in F8 layout mode). 0 = never hide.",
            new AcceptableValueRange<float>(0f, 120f)));

        ShowGuide = cfg.Bind("Guide", "Visible", true,
            "During a run, show an arrow around your character pointing the way to tracked quests that need a place (shrine, crypt, hidden item, enemy type). It shows only the direction, never the exact spot. Also toggled in game with the ToggleQuestGuide key.");
        GuideMinimap = cfg.Bind("Guide", "MinimapArrows", false, "Also put an arrow on the edge of the game's minimap pointing toward the quest target.");
        GuideScale = cfg.Bind("Guide", "Size", 1f, new ConfigDescription("Size of the guide arrows and labels (1 = default).", new AcceptableValueRange<float>(0.5f, 2.5f)));

        Language = cfg.Bind("Panel", "Language", "Auto", new ConfigDescription(
            "Language of the tracker's own labels. Auto = Arabic when the game is set to Arabic (Megabonk Arabic mod).",
            new AcceptableValueList<string>("Auto", "English", "Arabic")));

        CheckForUpdates = cfg.Bind("General", "CheckForUpdates", true,
            "Once per launch, ask Nexus if a newer Quest Tracker is out and show a small notice in the menus. Nothing is downloaded.");

        MaxPinned = cfg.Bind("Tracking", "MaxTrackedQuests", 3, new ConfigDescription("How many quests you can track at once.", new AcceptableValueRange<int>(1, 6)));
        Pinned = cfg.Bind("Tracking", "TrackedQuests", "", "Internal names of the quests you are tracking (managed by the mod, no need to edit).");
        TimerOverrides = cfg.Bind("Tracking", "TimerOverrides", "",
            "Advanced: force a timer for a quest. Format: questName=boss:30, otherQuest=stage:360 " +
            "(boss = timer starts when the boss spawns, stage = timer starts when the stage starts, values in seconds).");

        cfg.SaveOnConfigSet = false;
        cfg.Save();
    }
}
