using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace QuestTracker;

internal enum TimerAnchor { None, Stage, Boss, Info }

internal enum QuestStatus { Idle, InProgress, OnTrack, Hurry, Failed, Success, Complete, NotThisStage }

/// <summary>KeyOnly where clicking already does something in the game (claiming quest rewards).</summary>
internal enum HoverKind { KeyOnly, Clickable }

/// <summary>A quest the player has pinned, plus everything the panel needs to draw it.</summary>
internal class TrackedQuest
{
    public string Id;
    public MyAchievement Ach;
    public string Title = "";
    public string Description = "";

    public bool HasCount;
    public int Current, Target;
    public int RunStartValue = -1;
    public float Progress;

    public TimerAnchor Anchor;
    public float LimitSeconds;
    public bool FirstStageOnly;
    public bool NoDamage;

    public bool WasCompleteWhenPinned;
    public bool FailedThisStage;
    public bool SucceededThisRun;

    public QuestStatus Status = QuestStatus.Idle;
    public string StatusText = "";
    public string TimerText = "";
    public float TimerFraction = -1f; // remaining share of the time limit, or -1 when no bar
    public float FlashUntil;
    public ECharacter? RequiredCharacter; // set when the quest says "as Calcium", "with Ogre", ...
    public string RequiredCharacterName;
    public bool CharacterResolved;
    public QuestPlace Place; // map / tier / stage the quest needs; null until the game's data is loaded
    public int AlertLevel;   // highest boss-clock warning already shown this stage

    public float FinishedAt = -1f;      // unscaled time the quest became failed / completed, or -1
    public QuestStatus FinishedStatus;  // which of those it was
}

/// <summary>Holds the pinned quests and works out their live status. Only reads game data.</summary>
internal static class Tracker
{
    public static readonly List<TrackedQuest> Pinned = new();

    public static string HoveredId { get; private set; }
    public static HoverKind HoveredKind { get; private set; }
    private static MyAchievement _hovered;

    private static Dictionary<string, MyAchievement> _all = new();

    public static bool InRun { get; private set; }
    public static int StageIndex { get; private set; }
    private static float _stageStart = -1f;
    private static float _bossStart = -1f;
    private static bool _bossDefeated;
    private static float _bossKillSeconds = -1f;
    private static bool _loggedError;

    private static readonly Regex TimeRx = new(
        @"(\d+(?:[.,]\d+)?)\s*(seconds?|secs?|minutes?|mins?|s\b|m\b|ثانية|ثوان|ثواني|دقيقة|دقائق)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---------------------------------------------------------------- quest list

    public static IReadOnlyDictionary<string, MyAchievement> All
    {
        get
        {
            if (_all.Count == 0) LoadAll();
            return _all;
        }
    }

    public static void LoadAll()
    {
        var found = new Dictionary<string, MyAchievement>();
        try
        {
            var objects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MyAchievement>());
            foreach (var o in objects)
            {
                var a = o.TryCast<MyAchievement>();
                if (a == null || !a.isEnabled || string.IsNullOrEmpty(a.internalName)) continue;
                found[a.internalName] = a;
            }
        }
        catch (Exception e) { LogOnce("Could not read the quest list", e); }

        _all = found;
        foreach (var q in Pinned)
            if (q.Ach == null && _all.TryGetValue(q.Id, out var a)) Bind(q, a);
    }

    // ---------------------------------------------------------------- pinning

    public static void LoadPins()
    {
        Pinned.Clear();
        foreach (var id in Plugin.Cfg.Pinned.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Pinned.Add(new TrackedQuest { Id = id, Title = id });
    }

    private static void SavePins()
    {
        Plugin.Cfg.Pinned.Value = string.Join(",", Pinned.Select(p => p.Id));
        Plugin.Cfg.Save();
    }

    public static bool IsPinned(string id) => Pinned.Any(p => p.Id == id);

    public static void TogglePin(MyAchievement a)
    {
        if (a == null) return;
        var id = a.internalName;
        var existing = Pinned.FirstOrDefault(p => p.Id == id);
        if (existing != null)
        {
            Pinned.Remove(existing);
        }
        else
        {
            while (Pinned.Count >= Math.Max(1, Plugin.Cfg.MaxPinned.Value)) Pinned.RemoveAt(0);
            var q = new TrackedQuest { Id = id };
            Bind(q, a);
            q.WasCompleteWhenPinned = SafeIsCompleted(a);
            if (InRun) q.RunStartValue = q.Current;
            Pinned.Add(q);
        }
        SavePins();
    }

    public static void TogglePinHovered()
    {
        if (_hovered != null) TogglePin(_hovered);
    }

    public static void SetHovered(MyAchievement a, HoverKind kind)
    {
        if (a == null) return;
        _hovered = a;
        HoveredId = a.internalName;
        HoveredKind = kind;
    }

    public static void ClearHovered(MyAchievement a)
    {
        if (a == null || a.internalName == HoveredId) { _hovered = null; HoveredId = null; }
    }

    /// <summary>Click-to-track for places where clicking does nothing else useful (locked skins/unlocks, main-menu quests).</summary>
    public static void ClickTrack(MyAchievement a)
    {
        if (a == null || !Plugin.Cfg.ClickToTrack.Value) return;
        bool was = IsPinned(a.internalName);
        TogglePin(a);
        Hud.Toast(was ? L.T("Quest untracked", "تم إلغاء تتبع المهمة") : L.T("Quest tracked!", "تم تتبع المهمة!"));
    }

    private static void Bind(TrackedQuest q, MyAchievement a)
    {
        q.Ach = a;
        try
        {
            q.Title = Clean(a.GetDisplayName());
            var desc = Clean(a.GetUnlockRequirement());
            if (string.IsNullOrWhiteSpace(desc)) desc = Clean(a.GetUnlockDescription());
            q.Description = desc;
        }
        catch (Exception e) { LogOnce("Could not read quest text", e); }

        ParseRules(q);
        q.WasCompleteWhenPinned = SafeIsCompleted(a);
        ReadValues(q);
    }

    // ---------------------------------------------------------------- game events (detected by PollGame)

    public static void OnRunStarted()
    {
        StageIndex = 0;
        foreach (var q in Pinned)
        {
            q.SucceededThisRun = false;
            q.RunStartValue = -1;
        }
    }

    public static void OnStageStarted()
    {
        StageIndex++;
        _stageStart = Time.time;
        _bossStart = -1f;
        _bossDefeated = false;
        _bossKillSeconds = -1f;
        foreach (var q in Pinned) { q.FailedThisStage = false; q.AlertLevel = 0; }
    }

    public static void OnBossSpawned()
    {
        if (_bossStart < 0f) _bossStart = Time.time;
    }

    public static void OnBossDefeated()
    {
        _bossDefeated = true;
        if (_bossKillSeconds < 0f && _bossStart >= 0f) _bossKillSeconds = Time.time - _bossStart;
    }

    // ---------------------------------------------------------------- per-tick update

    public static void Tick()
    {
        try { PollGame(); }
        catch (Exception e) { InRun = false; LogOnce("Could not read run state", e); }

        if (Pinned.Any(p => p.Ach == null)) LoadAll();

        foreach (var q in Pinned)
        {
            if (q.Ach == null) { q.Status = QuestStatus.Idle; q.StatusText = L.T("Quest not found in this game version", "المهمة غير موجودة في هذه النسخة من اللعبة"); continue; }
            try { Update(q); }
            catch (Exception e) { LogOnce("Could not update a tracked quest", e); }

            var finished = IsFinished(q.Status) ? (q.Status == QuestStatus.Failed ? QuestStatus.Failed : QuestStatus.Complete) : QuestStatus.Idle;
            if (finished == QuestStatus.Idle) q.FinishedAt = -1f;
            else if (q.FinishedAt < 0f || q.FinishedStatus != finished) { q.FinishedAt = Time.unscaledTime; q.FinishedStatus = finished; }
        }

        Guide.Tick();
    }

    /// <summary>
    /// Reads run, stage and boss state straight from the game's managers. This replaces Harmony hooks:
    /// the game's compiler merges identical functions, so hooking them could crash the game.
    /// </summary>
    private static void PollGame()
    {
        var gm = GameManager.Instance;
        bool wasInRun = InRun;
        InRun = gm != null;

        if (!InRun)
        {
            if (wasInRun) { _stageStart = -1f; _bossStart = -1f; _bossDefeated = false; }
            _runPtr = IntPtr.Zero;
            _stage = -1;
            return;
        }

        // A new GameManager means a new run (this also catches restarts).
        if (gm.Pointer != _runPtr)
        {
            bool joinedLate = !wasInRun && _runPtr == IntPtr.Zero && Time.timeSinceLevelLoad > 5f;
            _runPtr = gm.Pointer;
            _stage = -1;
            OnRunStarted();
            if (joinedLate) _stageStart = Time.time;
        }

        int stage = Assets.Scripts.Managers.MapController.index;
        if (stage != _stage)
        {
            _stage = stage;
            OnStageStarted();
            StageIndex = stage + 1;
        }

        var em = Assets.Scripts.Managers.EnemyManager.Instance;
        if (em == null) return;
        var bosses = em.stageBosses;
        if (_bossStart < 0f && !_bossDefeated && bosses != null && bosses.Count > 0) OnBossSpawned();
        if (_bossStart >= 0f && !_bossDefeated && em.stageBossIsDead) OnBossDefeated();
    }

    private static IntPtr _runPtr;
    private static int _stage = -1;

    private static bool IsFinished(QuestStatus s) => s is QuestStatus.Failed or QuestStatus.Success or QuestStatus.Complete;

    /// <summary>
    /// Failed and completed quests leave the panel after a few seconds. They stay tracked:
    /// a failed one comes back on the next stage or run, and both can be removed in layout mode (F8).
    /// </summary>
    public static bool IsHidden(TrackedQuest q)
    {
        if (q.FinishedAt < 0f || !IsFinished(q.Status)) return false;
        float delay = q.FinishedStatus == QuestStatus.Failed ? Plugin.Cfg.HideFailedAfter.Value : Plugin.Cfg.HideCompletedAfter.Value;
        return delay > 0f && Time.unscaledTime - q.FinishedAt > delay;
    }

    public static void Untrack(TrackedQuest q)
    {
        if (!Pinned.Remove(q)) return;
        SavePins();
    }

    private static void Update(TrackedQuest q)
    {
        var before = q.Status;
        ReadValues(q);
        if (InRun && q.RunStartValue < 0) q.RunStartValue = q.Current;

        q.TimerText = "";
        q.TimerFraction = -1f;

        if (SafeIsCompleted(q.Ach))
        {
            q.Status = QuestStatus.Complete;
            q.StatusText = q.WasCompleteWhenPinned ? L.T("Already completed", "مكتملة مسبقا") : L.T("COMPLETE!", "اكتملت!");
            Flash(q, before);
            return;
        }

        if (q.SucceededThisRun)
        {
            q.Status = QuestStatus.Success;
            q.StatusText = L.T("Done! It unlocks when the game saves your progress", "تم! ستفتح عندما تحفظ اللعبة تقدمك");
            Flash(q, before);
            return;
        }

        if (!q.CharacterResolved) ResolveCharacter(q);
        q.Place ??= QuestPlaces.For(q);

        if (!InRun)
        {
            q.Status = QuestStatus.Idle;
            string toGo = LevelQuests.IsLevelQuest(q.Id)
                ? L.T($"Reach level {q.Target} in one run", $"صل إلى المستوى {q.Target} في جولة واحدة")
                : q.HasCount ? L.T($"{Math.Max(0, q.Target - q.Current):N0} to go", $"متبقي {Math.Max(0, q.Target - q.Current):N0}") : L.T("Start a run to track this quest", "ابدأ جولة لتتبع هذه المهمة");
            // Only the requirements the quest really has: "Play on Graveyard · Tier 2 as Calcium".
            var needs = new List<string>();
            if (q.Place != null && q.Place.Any) needs.Add(L.T($"Play on {q.Place.Describe()}", $"العب في {q.Place.Describe()}"));
            if (q.RequiredCharacter.HasValue) needs.Add(L.T($"as {q.RequiredCharacterName}", $"بشخصية {q.RequiredCharacterName}"));
            if (needs.Count == 0) q.StatusText = toGo;
            else
            {
                string need = string.Join(" ", needs);
                if (!q.Place?.Any ?? true) need = L.T($"Play as {q.RequiredCharacterName}", $"العب بشخصية {q.RequiredCharacterName}");
                q.StatusText = q.HasCount ? $"{need}  ·  {toGo}" : need;
            }
            return;
        }

        string wrongPlace = QuestPlaces.Mismatch(q.Place);
        if (wrongPlace != null)
        {
            q.Status = QuestStatus.NotThisStage;
            q.StatusText = L.T($"Needs {wrongPlace}: won't count this run", $"تحتاج {wrongPlace}: لن تحتسب هذه الجولة");
            return;
        }

        if (q.RequiredCharacter.HasValue)
        {
            var current = CurrentCharacter();
            if (current.HasValue && current.Value != q.RequiredCharacter.Value)
            {
                q.Status = QuestStatus.Failed;
                q.StatusText = L.T($"Needs {q.RequiredCharacterName}: won't count this run", $"تحتاج {q.RequiredCharacterName}: لن تحتسب هذه الجولة");
                return;
            }
        }

        if (q.FirstStageOnly && StageIndex > 1)
        {
            q.Status = QuestStatus.NotThisStage;
            q.StatusText = L.T("Only counts on the first stage", "تحتسب في المرحلة الأولى فقط");
            return;
        }

        if (q.NoDamage && SafeTookDamage())
        {
            q.Status = QuestStatus.Failed;
            q.StatusText = L.T("FAILED: you took damage this run", "فشلت: تلقيت ضررا في هذه الجولة");
            return;
        }

        switch (q.Anchor)
        {
            case TimerAnchor.Stage: UpdateTimer(q, _stageStart, L.T("Stage clock", "وقت المرحلة")); return;
            case TimerAnchor.Boss:
                if (_bossStart < 0f && !_bossDefeated)
                {
                    q.Status = QuestStatus.InProgress;
                    q.TimerText = FormatTime(q.LimitSeconds);
                    q.StatusText = L.T($"Waiting for the boss. You'll have {FormatTime(q.LimitSeconds)}", $"بانتظار الزعيم. أمامك {FormatTime(q.LimitSeconds)}");
                    return;
                }
                UpdateTimer(q, _bossStart, L.T("Boss fight", "قتال الزعيم"));
                return;
            case TimerAnchor.Info:
                q.Status = QuestStatus.InProgress;
                q.TimerText = FormatTime(q.LimitSeconds);
                q.StatusText = L.T($"Time limit: {FormatTime(q.LimitSeconds)}", $"الوقت المحدد: {FormatTime(q.LimitSeconds)}");
                return;
        }

        q.Status = QuestStatus.InProgress;
        if (q.HasCount)
        {
            int left = Math.Max(0, q.Target - q.Current);
            int gained = q.RunStartValue >= 0 && !LevelQuests.IsLevelQuest(q.Id) ? Math.Max(0, q.Current - q.RunStartValue) : 0;
            if (LevelQuests.IsLevelQuest(q.Id))
                q.StatusText = left > 0 ? L.T($"{left} more levels this run", $"{left} مستويات أخرى في هذه الجولة") : L.T("Level reached!", "تم الوصول إلى المستوى!");
            else q.StatusText = gained > 0 ? L.T($"{left:N0} left  ·  +{gained:N0} this run", $"متبقي {left:N0}  ·  +{gained:N0} في هذه الجولة") : L.T($"{left:N0} left", $"متبقي {left:N0}");
        }
        else
        {
            q.StatusText = q.NoDamage ? L.T("No damage taken so far", "لم تتلق أي ضرر حتى الآن") : L.T("In progress", "قيد التنفيذ");
        }
    }

    private static void UpdateTimer(TrackedQuest q, float start, string label)
    {
        if (start < 0f) start = Time.time;

        if (_bossDefeated && _bossKillSeconds >= 0f && (q.Anchor == TimerAnchor.Boss || MentionsBoss(q)))
        {
            bool inTime = _bossKillSeconds <= q.LimitSeconds;
            q.TimerText = FormatTime(_bossKillSeconds);
            q.TimerFraction = inTime ? 1f : 0f;
            if (inTime) { q.SucceededThisRun = true; q.Status = QuestStatus.Success; q.StatusText = L.T($"Boss down in {FormatTime(_bossKillSeconds)}!", $"سقط الزعيم في {FormatTime(_bossKillSeconds)}!"); }
            else { q.FailedThisStage = true; q.Status = QuestStatus.Failed; q.StatusText = L.T($"FAILED: boss took {FormatTime(_bossKillSeconds)}", $"فشلت: استغرق الزعيم {FormatTime(_bossKillSeconds)}"); }
            return;
        }

        float elapsed = Time.time - start;
        float left = q.LimitSeconds - elapsed;

        if (left <= 0f || q.FailedThisStage)
        {
            q.FailedThisStage = true;
            q.Status = QuestStatus.Failed;
            q.TimerText = "0:00";
            q.TimerFraction = 0f;
            q.StatusText = q.Anchor == TimerAnchor.Boss ? L.T("FAILED: too slow. Next boss!", "فشلت: كنت بطيئا. الزعيم القادم!") : L.T("FAILED: time's up", "فشلت: انتهى الوقت");
            return;
        }

        q.TimerFraction = Mathf.Clamp01(left / Math.Max(1f, q.LimitSeconds));
        q.TimerText = FormatTime(left);
        q.Status = q.TimerFraction < 0.25f ? QuestStatus.Hurry : QuestStatus.OnTrack;
        q.StatusText = q.Status == QuestStatus.Hurry ? L.T($"HURRY! {label}", $"أسرع! {label}") : L.T($"On track · {label}", $"على المسار · {label}");

        if (q.Anchor == TimerAnchor.Stage && MentionsBoss(q)) BossClockAdvice(q, left);
    }

    /// <summary>
    /// "Kill the boss in under 6 minutes": the clock runs from the stage start, so the player has to find the
    /// Boss Portal and summon the boss in time. Tells them what to do and warns them as the clock runs down.
    /// </summary>
    private static void BossClockAdvice(TrackedQuest q, float left)
    {
        if (_bossStart >= 0f || _bossDefeated)
        {
            q.StatusText = L.T("Boss is up: kill it before the clock runs out!", "الزعيم ظهر: اقتله قبل أن ينتهي الوقت!");
            return;
        }

        float tooLate = Math.Max(45f, q.LimitSeconds * 0.12f); // a boss fight takes a while
        int level = left <= tooLate ? 3 : q.TimerFraction <= 0.25f ? 2 : q.TimerFraction <= 0.5f ? 1 : 0;
        string text = level switch
        {
            3 => L.T("Probably too late: the boss takes time to kill", "على الأغلب فات الأوان: قتل الزعيم يحتاج وقتا"),
            2 => L.T("Summon the boss NOW!", "استدعِ الزعيم الآن!"),
            1 => L.T("Half the time is gone: summon the boss soon", "مضى نصف الوقت: استدعِ الزعيم قريبا"),
            _ => L.T("Find the Boss Portal and summon the boss", "ابحث عن بوابة الزعيم واستدعِ الزعيم"),
        };
        q.StatusText = text;

        if (level > q.AlertLevel)
        {
            q.AlertLevel = level;
            if (level > 0) Hud.Toast($"{q.Title}: {text}", level >= 2 ? Hud.RedColor : Hud.GoldColor, 5f);
        }
    }

    private static void Flash(TrackedQuest q, QuestStatus before)
    {
        if (before != q.Status && !q.WasCompleteWhenPinned) q.FlashUntil = Time.unscaledTime + 3f;
    }

    // ---------------------------------------------------------------- reading values

    private static void ReadValues(TrackedQuest q)
    {
        var a = q.Ach;
        if (a == null) return;
        try
        {
            q.HasCount = a.IsUsingTargetValue() && a.targetValue > 1;
            if (q.HasCount)
            {
                q.Target = a.targetValue;
                q.Current = Math.Max(0, a.GetCurrentValue());
                q.Progress = Mathf.Clamp01(q.Current / (float)Math.Max(1, q.Target));
            }
            else
            {
                float p = a.GetProgress();
                q.Progress = Mathf.Clamp01(p > 1.001f ? p / 100f : p);
            }

            // Level quests: the game's counter stays 0 until done, so show the level reached in this run instead.
            if (LevelQuests.IsLevelQuest(q.Id) && a.targetValue > 0)
            {
                q.HasCount = true;
                q.Target = a.targetValue;
                q.Current = InRun ? Math.Max(0, LevelQuests.Live(q.Id)) : 0;
                q.Progress = Mathf.Clamp01(q.Current / (float)Math.Max(1, q.Target));
            }
        }
        catch (Exception e) { LogOnce("Could not read quest progress", e); }
    }

    private static void ParseRules(TrackedQuest q)
    {
        string d = q.Description ?? "";
        string lower = d.ToLowerInvariant();

        q.FirstStageOnly = Regex.IsMatch(lower, @"\b(first stage|stage 1|1st stage|first level|level 1)\b|(^|\s)أول\s|المرحلة الأولى");
        q.NoDamage = Regex.IsMatch(lower, @"without (taking|getting) (any )?(damage|hit)|no damage|taking no damage|دون أن تتلقى أي ضرر|دون تلقي أي ضرر|لا تتلق أي ضرر");

        q.Anchor = TimerAnchor.None;
        q.LimitSeconds = 0f;

        if (TryOverride(q.Id, out var anchor, out var secs)) { q.Anchor = anchor; q.LimitSeconds = secs; return; }

        // Known game quest: kill a stage boss quickly. The game itself measures from boss spawn.
        string bossBuster = SafeString(() => AchievementTracker.a_bossBuster);
        if (!string.IsNullOrEmpty(bossBuster) && q.Id == bossBuster)
        {
            q.Anchor = TimerAnchor.Boss;
            q.LimitSeconds = TargetFloat(q.Ach, 30f);
            return;
        }

        bool timed = Regex.IsMatch(lower, @"\b(under|within|in less than|or less|before)\b|في أقل من|خلال|قبل");
        var m = TimeRx.Match(d);
        if (!timed || !m.Success) return;

        float value = float.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        bool minutes = m.Groups[2].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase) || m.Groups[2].Value.StartsWith("دق");
        q.LimitSeconds = minutes ? value * 60f : value;

        if (lower.Contains("miniboss") || lower.Contains("mini boss") || lower.Contains("elite") || lower.Contains("زعيم مصغر") || lower.Contains("النخبة"))
            q.Anchor = TimerAnchor.Info; // no reliable spawn event for these yet
        else if ((lower.Contains("boss") || lower.Contains("زعيم")) && !minutes)
            q.Anchor = TimerAnchor.Boss;
        else
            q.Anchor = TimerAnchor.Stage;
    }

    private static bool TryOverride(string id, out TimerAnchor anchor, out float seconds)
    {
        anchor = TimerAnchor.None;
        seconds = 0f;
        foreach (var part in Plugin.Cfg.TimerOverrides.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !kv[0].Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
            var rule = kv[1].Split(':', 2, StringSplitOptions.TrimEntries);
            if (rule.Length != 2 || !float.TryParse(rule[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) continue;
            anchor = rule[0].Equals("boss", StringComparison.OrdinalIgnoreCase) ? TimerAnchor.Boss : TimerAnchor.Stage;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- character requirements

    private static List<(string Name, ECharacter Character)> _characterNames;

    /// <summary>Character names as the game shows them (plus the internal enum names), longest first.</summary>
    private static List<(string Name, ECharacter Character)> CharacterNames()
    {
        if (_characterNames != null) return _characterNames;

        var names = new List<(string, ECharacter)>();
        bool fromGame = false;
        try
        {
            var dm = DataManager.Instance;
            if (dm != null && dm.characterData != null)
            {
                foreach (var kv in dm.characterData)
                {
                    string display = kv.Value != null ? Clean(kv.Value.GetName()) : "";
                    if (display.Length > 0) { names.Add((display, kv.Key)); fromGame = true; }
                }
            }
        }
        catch (Exception e) { LogOnce("Could not read character names", e); }

        foreach (ECharacter c in Enum.GetValues(typeof(ECharacter)))
            names.Add((c.ToString(), c));

        var sorted = names.OrderByDescending(n => n.Item1.Length).ToList();
        if (fromGame) _characterNames = sorted; // keep retrying until the game's own names are loaded
        return sorted;
    }

    /// <summary>Finds a required character in quest text such as "Kill 15,000 enemies as Ogre".</summary>
    private static void ResolveCharacter(TrackedQuest q)
    {
        string d = q.Description ?? "";
        if (d.Length == 0) return;

        foreach (var (name, character) in CharacterNames())
        {
            var rx = new Regex(@"(\b(as|with|using|playing)\s+(the\s+)?|بشخصية\s+)" + Regex.Escape(name) + @"(?![\p{L}\p{M}])", RegexOptions.IgnoreCase);
            if (!rx.IsMatch(d)) continue;
            q.RequiredCharacter = character;
            q.RequiredCharacterName = name;
            break;
        }
        q.CharacterResolved = _characterNames != null;
    }

    /// <summary>The character the local player is using this run (falls back to the one picked in the menu).</summary>
    private static ECharacter? CurrentCharacter()
    {
        try
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.player != null) return gm.player.character;
        }
        catch { }
        try { return CharacterMenu.selectedCharacter; }
        catch { return null; }
    }

    private static bool MentionsBoss(TrackedQuest q) =>
        (q.Description ?? "").IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 || (q.Description ?? "").Contains("زعيم");

    private static float TargetFloat(MyAchievement a, float fallback)
    {
        try
        {
            if (a.targetValueFloat > 0f) return a.targetValueFloat;
            if (a.targetValue > 0) return a.targetValue;
        }
        catch { }
        return fallback;
    }

    private static bool SafeIsCompleted(MyAchievement a)
    {
        try { return a != null && a.IsCompleted(); } catch { return false; }
    }

    private static bool SafeTookDamage()
    {
        try { return AchievementTracker.hasTakenDamageThisRun; } catch { return false; }
    }

    private static string SafeString(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }

    // ---------------------------------------------------------------- helpers

    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int s = Mathf.CeilToInt(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return Regex.Replace(s, "<.*?>", "").Trim();
    }

    private static void LogOnce(string what, Exception e)
    {
        if (_loggedError) return;
        _loggedError = true;
        Plugin.Logger.LogWarning($"{what}: {e.Message} (further errors hidden)");
    }

    /// <summary>Exports every quest with its progress, for debugging and bug reports.</summary>
    public static string ExportAll()
    {
        LoadAll();
        var sb = new StringBuilder("internalName\ttype\tdifficulty\tname\trequirement\ttargetValue\ttargetValueFloat\tcurrent\tcompleted\n");
        foreach (var a in _all.Values.OrderBy(x => (int)x.achievementType).ThenBy(x => x.internalName))
        {
            try
            {
                sb.Append(a.internalName).Append('\t')
                  .Append(a.achievementType).Append('\t')
                  .Append(a.difficulty).Append('\t')
                  .Append(Clean(a.GetDisplayName())).Append('\t')
                  .Append(Clean(a.GetUnlockRequirement())).Append('\t')
                  .Append(a.targetValue).Append('\t')
                  .Append(a.targetValueFloat.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(a.IsUsingTargetValue() ? a.GetCurrentValue() : 0).Append('\t')
                  .Append(a.IsCompleted()).Append('\n');
            }
            catch (Exception e) { sb.Append(a.internalName).Append("\terror: ").Append(e.Message).Append('\n'); }
        }
        var path = Path.Combine(Paths.BepInExRootPath, "QuestTracker_quests.tsv");
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
