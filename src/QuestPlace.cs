using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Managers;

namespace QuestTracker;

/// <summary>Where a quest has to be done. Zero / None means the quest doesn't care.</summary>
internal class QuestPlace
{
    public EMap Map;
    public int Tier;  // 1-3
    public int Stage; // 1-3
    public bool StageFrom; // true: "from stage N on" (an enemy that starts spawning there), false: "on stage N"

    public bool Any => Map != EMap.None || Tier > 0 || Stage > 0;

    /// <summary>"Graveyard · Tier 2 · from stage 2", only with the parts the quest needs.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Map != EMap.None) parts.Add(QuestPlaces.MapName(Map));
        if (Tier > 0) parts.Add(L.T($"Tier {Tier}", $"المستوى {Tier}"));
        if (Stage > 0 && StageFrom) parts.Add(L.T($"from stage {Stage}", $"من المرحلة {Stage}"));
        else if (Stage > 0) parts.Add(L.T($"stage {Stage}", $"المرحلة {Stage}"));
        return string.Join("  ·  ", parts);
    }
}

/// <summary>
/// Works out a quest's map, tier and stage from its text (English or Arabic), the guide's own rules,
/// and, for "kill this enemy" quests, the map and stage where the game spawns that enemy.
/// </summary>
internal static class QuestPlaces
{
    private static readonly (Regex Rx, EMap Map)[] MapWords =
    {
        (new Regex(@"\bforest\b|الغابة", RegexOptions.IgnoreCase | RegexOptions.Compiled), EMap.Forest),
        (new Regex(@"\bdesert\b|الصحراء", RegexOptions.IgnoreCase | RegexOptions.Compiled), EMap.Desert),
        (new Regex(@"\bgraveyard\b|المقبرة", RegexOptions.IgnoreCase | RegexOptions.Compiled), EMap.Graveyard),
    };

    // Arabic uses "المستوى" for tiers and for levels ("level the tome to 9"), so it only counts after a map, "run" or "on".
    private static readonly Regex TierRx = new(@"\btier\s*([1-3])\b|(?:الغابة|الصحراء|المقبرة|جولة|في)\s+المستوى\s*([1-3])(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StageRx = new(@"\bstage\s*([1-3])\b|المرحلة\s*([1-3])(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string MapName(EMap map) => map switch
    {
        EMap.Forest => L.T("Forest", "الغابة"),
        EMap.Desert => L.T("Desert", "الصحراء"),
        EMap.Graveyard => L.T("Graveyard", "المقبرة"),
        _ => map.ToString(),
    };

    /// <summary>Null until the game's data is loaded; then the place (which may be empty).</summary>
    public static QuestPlace For(TrackedQuest q)
    {
        var place = new QuestPlace();
        string text = q.Description ?? "";

        // Only one map named in the text means the quest is tied to it ("Complete Desert Tier 2").
        EMap named = EMap.None;
        int maps = 0;
        foreach (var (rx, map) in MapWords)
            if (rx.IsMatch(text)) { named = map; maps++; }
        if (maps == 1) place.Map = named;

        var tier = TierRx.Match(text);
        if (tier.Success) place.Tier = int.Parse(tier.Groups[1].Success ? tier.Groups[1].Value : tier.Groups[2].Value);

        var stage = StageRx.Match(text);
        if (stage.Success) place.Stage = int.Parse(stage.Groups[1].Success ? stage.Groups[1].Value : stage.Groups[2].Value);

        var rule = GuideTargets.For(q);
        if (place.Map == EMap.None && rule != null) place.Map = rule.Map;

        // "Kill Spooky Steve": ask the game where that enemy spawns.
        var enemy = rule == null ? null : GuideTargets.EnemyFor(rule.Kinds[0]);
        if (enemy.HasValue)
        {
            var dm = DataManager.Instance;
            if (dm == null || dm.enemyData == null) return null; // not loaded yet, try again later
            try
            {
                if (dm.enemyData.ContainsKey(enemy.Value))
                {
                    var data = dm.enemyData[enemy.Value];
                    var m = data.maps;
                    if (place.Map == EMap.None && m != EMap.None && (m & (m - 1)) == 0) place.Map = m; // exactly one map
                    if (place.Stage == 0 && data.minStage > 0) { place.Stage = data.minStage + 1; place.StageFrom = true; } // minStage is 0-based
                }
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"Could not read enemy spawn data: {e.Message}"); }
        }
        return place;
    }

    public static EMap CurrentMap()
    {
        try { return MapController.currentMap != null ? MapController.currentMap.eMap : EMap.None; }
        catch { return EMap.None; }
    }

    /// <summary>The tier of the current run (1-3), or 0 when unknown.</summary>
    public static int CurrentTier()
    {
        try { return MapController.runConfig != null ? MapController.runConfig.mapTierIndex + 1 : 0; }
        catch { return 0; }
    }

    /// <summary>What's wrong with this run for the quest (wrong map or tier), or null when it's fine.</summary>
    public static string Mismatch(QuestPlace p)
    {
        if (p == null || !p.Any) return null;
        var map = CurrentMap();
        int tier = CurrentTier();
        bool wrongMap = p.Map != EMap.None && map != EMap.None && p.Map != map;
        bool wrongTier = p.Tier > 0 && tier > 0 && p.Tier != tier;
        if (!wrongMap && !wrongTier) return null;
        var need = new QuestPlace { Map = p.Map, Tier = p.Tier };
        return need.Describe();
    }
}
