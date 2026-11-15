using System;
using System.Collections.Generic;
using System.Linq;
using Actors.Enemies;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups.Chests;
using Assets.Scripts.Inventory__Items__Pickups.Interactables;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Managers;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestTracker;

/// <summary>One place the guide is pointing at for a tracked quest.</summary>
internal class GuideTarget
{
    public TrackedQuest Quest;
    public Transform Transform;
    public string Label;
    public Color Color;
    public float Height; // how far above the object's origin to point
    public bool Find;    // exists but not found yet: show "Find the ..." with no arrow
}

/// <summary>
/// Guides the player through their tracked quests: a compass arrow around the character for things they have
/// already found, and a "Find the ..." objective for things they haven't (no direction, so no cheating).
/// Only reads game state.
/// </summary>
internal static class Guide
{
    private static readonly Color[] QuestColors = { new(1f, 0.82f, 0.25f), new(0.45f, 0.85f, 1f), new(1f, 0.5f, 0.85f) };

    private static readonly List<GuideTarget> Targets = new();
    private static string _mapHint;
    private static float _mapHintUntil;
    private static int _hintStage = -1;
    private static float _nextScan;
    private static bool _loggedError;

    private static readonly HashSet<IntPtr> OurMinimapArrows = new();
    private static readonly Dictionary<IntPtr, Transform> MinimapTransforms = new();
    private static bool _minimapBroken;

    private static Texture2D _arrow;

    public static bool Visible => Plugin.Cfg.ShowGuide.Value;

    // ---------------------------------------------------------------- finding targets (Tracker.Tick, 5x per second)

    public static void Tick()
    {
        if (!Tracker.InRun || !Visible)
        {
            Targets.Clear();
            ClearMinimap();
            _hintStage = -1;
            return;
        }
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.5f;

        try { Scan(); }
        catch (Exception e) { Targets.Clear(); LogOnce("Could not find quest targets", e); }

        try { SyncMinimap(); }
        catch (Exception e) { _minimapBroken = true; LogOnce("Could not mark the minimap", e); }
    }

    private static void Scan()
    {
        Targets.Clear();
        var player = PlayerPosition();
        var wrongMap = new List<string>();
        RefreshAreas();
        _playerArea = AreaOf(player);
        _cam = Camera.main;
        _player = player;
        _minimapRange = MinimapRange();
        int runStage = Tracker.StageIndex;
        if (runStage != _discoveredStage) { Discovered.Clear(); _discoveredStage = runStage; }

        int colorIndex = 0;
        foreach (var q in Tracker.Pinned)
        {
            if (q.Ach == null || Tracker.IsHidden(q)) continue;
            if (q.Status is QuestStatus.Complete or QuestStatus.Success) continue;

            // Wrong map or tier: say where to play instead of pointing anywhere.
            string wrongPlace = QuestPlaces.Mismatch(q.Place);
            if (wrongPlace != null)
            {
                wrongMap.Add(L.T($"{q.Title}: play on {wrongPlace}", $"{q.Title}: العب في {wrongPlace}"));
                continue;
            }
            if (q.Status is QuestStatus.Failed or QuestStatus.NotThisStage) continue;

            var rule = GuideTargets.For(q);
            if (rule == null) continue;
            var color = QuestColors[colorIndex++ % QuestColors.Length];

            // An enemy that only spawns later in the run: say when instead of pointing.
            if (q.Place != null && q.Place.StageFrom && Tracker.StageIndex < q.Place.Stage)
            {
                wrongMap.Add(L.T($"{q.Title}: shows up from stage {q.Place.Stage}", $"{q.Title}: يظهر من المرحلة {q.Place.Stage}"));
                continue;
            }

            var target = FirstTarget(rule, player);

            // Inside a crypt and the target is out on the map: lead the player out first.
            if (target.Transform == null && !target.Find && _playerArea >= 0)
            {
                _ignoreAreas = true;
                var outside = FirstTarget(rule, player);
                _ignoreAreas = false;
                if (outside.Transform != null || outside.Find)
                {
                    _sawHidden = false;
                    var exit = Closest<InteractableCryptLeave>(player, c => !c.hasInteracted, L.T("Leave the crypt", "اخرج من السرداب"));
                    if (exit.Item1 != null) target = (exit.Item1, exit.Item2, exit.Item3, false);
                    else if (_sawHidden) target = (null, L.T("Crypt exit", "مخرج السرداب"), 0f, true);
                }
            }

            if (target.Transform != null)
                Targets.Add(new GuideTarget { Quest = q, Transform = target.Transform, Label = target.Label, Color = color, Height = target.Height });
            else if (target.Find) // it exists, but the player hasn't found it yet: only say what to look for
                Targets.Add(new GuideTarget { Quest = q, Label = L.T($"Find the {target.Label}", $"ابحث عن {target.Label}"), Color = color, Find = true });
        }

        // The "wrong map" hint shows for a few seconds at the start of each stage, then gets out of the way.
        if (Tracker.StageIndex != _hintStage)
        {
            _hintStage = Tracker.StageIndex;
            _mapHint = wrongMap.Count > 0 ? string.Join("\n", wrongMap) : null;
            _mapHintUntil = -1f; // starts counting once it is on screen (not during the loading screen)
        }
    }

    /// <summary>
    /// The first step of the quest that exists right now. Transform is set when the player has already found it
    /// (arrow); Find is true when it exists but hasn't been found yet (only a "Find the ..." line, no direction).
    /// </summary>
    private static (Transform Transform, string Label, float Height, bool Find) FirstTarget(GuideRule rule, Vector3 player)
    {
        foreach (var kind in rule.Kinds)
        {
            _sawHidden = false;
            var found = Nearest(kind, player);
            if (found.Transform != null) return (found.Transform, found.Label, found.Height, false);
            if (_sawHidden) return (null, found.Label, 0f, true);
        }
        return default;
    }

    // ---------------------------------------------------------------- discovery: no arrows to things the player hasn't found

    private static readonly HashSet<IntPtr> Discovered = new();
    private static int _discoveredStage = -1;
    private static bool _sawHidden; // the last search skipped something that exists but isn't discovered yet
    private static Camera _cam;
    private static Vector3 _player;
    private static float _minimapRange; // world radius the minimap shows around the player (0 = unknown)

    /// <summary>How far the game's minimap reaches: its camera looks straight down, so that's its orthographic size.</summary>
    private static float MinimapRange()
    {
        try
        {
            var p = MyPlayer.Instance;
            var mc = p != null ? p.minimapCamera : null;
            if (mc != null && mc.orthographic) return mc.orthographicSize * 0.95f; // a little inside the round edge
        }
        catch { }
        return 0f;
    }

    /// <summary>
    /// True once the player has found this object: walked close to it, or had it on screen with nothing in the way.
    /// Remembered for the rest of the stage.
    /// </summary>
    private static bool IsDiscovered(Transform t)
    {
        if (Discovered.Contains(t.Pointer)) return true;
        if (!SeenNow(t)) { _sawHidden = true; return false; }
        Discovered.Add(t.Pointer);
        return true;
    }

    private static bool SeenNow(Transform t)
    {
        var pos = t.position;
        float d = Vector3.Distance(_player, pos);
        if (d < 18f) return true;

        // On the game's minimap: the player can already see it there, so it counts as found.
        var flat = pos - _player; flat.y = 0f;
        if (_minimapRange > 0f && flat.magnitude <= _minimapRange) return true;

        if (d > 75f || _cam == null) return false;

        var target = pos + Vector3.up;
        var vp = _cam.WorldToViewportPoint(target);
        if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;

        // Line of sight from the camera: walls and terrain block it, the object itself doesn't.
        var from = _cam.transform.position;
        var dir = target - from;
        float len = dir.magnitude;
        if (!Physics.Raycast(from, dir / len, out var hit, len - 1.5f, ~0, QueryTriggerInteraction.Ignore)) return true;
        var root = t.root;
        return hit.transform != null && (hit.transform == t || hit.transform.IsChildOf(root));
    }

    // ---------------------------------------------------------------- areas: crypts are closed off from the map

    private static readonly List<(Bounds Box, int Area)> Areas = new();
    private static readonly HashSet<IntPtr> AreaSources = new();
    private static int _playerArea = -1;
    private static bool _ignoreAreas;

    /// <summary>
    /// Collects the space each crypt takes up (the game builds crypts from pieces, plus a start and an end room).
    /// A crypt can only be left through its exit door, so targets on the other side of its walls aren't reachable.
    /// </summary>
    private static void RefreshAreas()
    {
        var controllers = Object.FindObjectsOfType(Il2CppType.Of<RsgController>());
        var now = new HashSet<IntPtr>();
        foreach (var o in controllers) now.Add(o.Pointer);
        if (now.SetEquals(AreaSources) && Areas.Count > 0) return; // crypts don't move: keep the boxes
        Areas.Clear();
        AreaSources.Clear();

        int area = 0;
        foreach (var o in controllers)
        {
            var rc = o.TryCast<RsgController>();
            if (rc == null) continue;
            AreaSources.Add(o.Pointer);
            try
            {
                if (rc.allPieces != null)
                    foreach (var piece in rc.allPieces)
                        if (piece != null) AddArea(piece.GetBounds(), area);
                if (rc.rsgStart != null) AddRendererArea(rc.rsgStart.gameObject, area);
                if (rc.rsgEnd != null) AddRendererArea(rc.rsgEnd.transform.parent != null ? rc.rsgEnd.transform.parent.gameObject : rc.rsgEnd.gameObject, area);
            }
            catch (Exception e) { LogOnce("Could not read a crypt's shape", e); }
            area++;
        }
    }

    private static void AddRendererArea(GameObject root, int area)
    {
        bool any = false;
        var box = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { box = r.bounds; any = true; }
            else box.Encapsulate(r.bounds);
        }
        if (any && box.size.magnitude < 300f) AddArea(box, area); // ignore anything that spans the whole map
    }

    private static void AddArea(Bounds b, int area)
    {
        b.Expand(4f); // doorways and the player standing at the edge
        Areas.Add((b, area));
    }

    private static int AreaOf(Vector3 p)
    {
        foreach (var (box, area) in Areas)
            if (box.Contains(p)) return area;
        return -1; // out on the open map
    }

    /// <summary>True when the player can walk to this spot from where they are (same crypt, or both outside).</summary>
    private static bool InReach(Vector3 p) => _ignoreAreas || AreaOf(p) == _playerArea;

    private static Vector3 PlayerPosition()
    {
        try
        {
            var p = MyPlayer.Instance;
            if (p != null) return p.transform.position;
        }
        catch { }
        var cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    // ---------------------------------------------------------------- what each kind of target is in the game

    private static (Transform Transform, string Label, float Height) Nearest(TargetKind kind, Vector3 from)
    {
        switch (kind)
        {
            case TargetKind.Boombox: return Closest<InteractableBoombox>(from, b => !b.done, L.T("Boombox", "مكبر الصوت"));
            case TargetKind.MonkeCage: return Closest<InteractableCage>(from, c => !c.done, L.T("Monke's cage", "قفص مونكي"));
            case TargetKind.Bush: return Closest<InteractableCharacterFight>(from, f => !f.done && IsEnemy(f.enemyData, EEnemy.Bush), L.T("Bush", "الشجيرة"));
            case TargetKind.Bandit: return Closest<InteractableCharacterFight>(from, f => !f.done && IsEnemy(f.enemyData, EEnemy.Bandit), L.T("Bandit", "اللص"));
            case TargetKind.Bananarang: return Closest<InteractableBananarang>(from, b => !b.done, L.T("Hidden banana", "الموزة المخفية"));
            case TargetKind.Gift: return Closest<InteractableGift>(from, g => !g.done, L.T("Hidden gift", "الهدية المخفية"));
            case TargetKind.FrogEgg: return Closest<InteractableEgg>(from, e => !e.done, L.T("Frog egg", "بيضة الضفادع"));
            case TargetKind.QuestUnlock: return Closest<InteractableUnlock>(from, u => !u.done, L.T("Hidden item", "غرض مخفي"));
            case TargetKind.SkeletonStatue: return Closest<InteractableSkeletonKingStatue>(from, s => !s.done, L.T("Undead statue", "تمثال الموتى الأحياء"));
            case TargetKind.DesertGrave: return Closest<InteractableDesertGrave>(from, g => !g.done, L.T("Grave", "قبر"));
            case TargetKind.Coffin: return Closest<InteractableCoffin>(from, c => !c.interacted, L.T("Coffin", "تابوت"));
            case TargetKind.Crypt: return Closest<InteractableCrypt>(from, c => !c.isDone, L.T("Crypt", "السرداب"));
            case TargetKind.CryptExit: return Closest<InteractableCryptLeave>(from, c => !c.hasInteracted, L.T("Crypt exit", "مخرج السرداب"));
            case TargetKind.CryptLoot:
            {
                var pot = Closest<InteractablePot>(from, p => p.isInCrypt && !p.broken, L.T("Crypt pot", "جرة السرداب"));
                var chest = Closest<InteractableChest>(from, c => c.isInCrypt && !c.opening, L.T("Crypt chest", "صندوق السرداب"));
                return Nearer(from, pot, chest);
            }
            case TargetKind.ChargeShrine: return Closest<ChargeShrine>(from, s => !s.completed && !s.rewardGiven, L.T("Charge shrine", "مزار الشحن"));
            case TargetKind.MagnetShrine: return Closest<InteractableShrineMagnet>(from, s => !s.done, L.T("Magnet shrine", "مزار الشفط"));
            case TargetKind.CursedShrine: return Closest<InteractableShrineCursed>(from, s => !s.done, L.T("Boss curse shrine", "مزار لعنة الزعيم"));
            case TargetKind.Microwave: return Closest<InteractableMicrowave>(from, m => m.usesLeft > 0, L.T("Microwave", "الميكروويف"));
            case TargetKind.ShadyCommon: return Shady(from, EItemRarity.Common);
            case TargetKind.ShadyRare: return Shady(from, EItemRarity.Rare);
            case TargetKind.ShadyEpic: return Shady(from, EItemRarity.Epic);
            case TargetKind.ShadyLegendary: return Shady(from, EItemRarity.Legendary);
            case TargetKind.Chest: return Closest<InteractableChest>(from, c => !c.opening && !c.isInCrypt, L.T("Chest", "صندوق"));
            case TargetKind.Pot: return Closest<InteractablePot>(from, p => !p.broken, L.T("Pot", "جرة"));
            case TargetKind.Tumbleweed: return Closest<InteractableTumbleWeed>(from, t => !t.broken, L.T("Tumbleweed", "شجيرة متدحرجة"));
            case TargetKind.Boss: return Boss(from);
            case TargetKind.Miniboss: return ClosestEnemy(from, e => IsMiniboss(e), L.T("Miniboss", "زعيم مصغر"));
            case TargetKind.Frogs: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Frog", "ضفدع"));
            case TargetKind.Scorpion: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Scorpion miniboss", "العقربوسي"));
            case TargetKind.Skeletons: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Skeleton", "هيكل عظمي"));
            case TargetKind.Goblins: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Goblin", "عفريت"));
            case TargetKind.Spectres: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Spectre", "طيف"));
            case TargetKind.Cactus: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Cactus", "صبار"));
            case TargetKind.SpookySteve: return ClosestEnemy(from, e => GuideTargets.Matches(e.enemyData.enemyName, kind), L.T("Spooky Steve", "ستيف المخيف"));
            case TargetKind.Pumpkin: return Closest<InteractablePot>(from, p => !p.broken && IsPumpkin(p), L.T("Pumpkin", "يقطينة"));
        }
        return default;
    }

    private static (Transform, string, float) Shady(Vector3 from, EItemRarity rarity) =>
        Closest<InteractableShadyGuy>(from, s => !s.done && s.rarity == rarity, L.T("Shady Guy", "الرجل المريب"));

    /// <summary>
    /// The boss steps in order: a living boss, else the exit portal once it opens, else the Boss Portal that
    /// summons the boss. Each only gets an arrow after the player has found it; before that, "Find the ...".
    /// </summary>
    private static (Transform, string, float) Boss(Vector3 from)
    {
        var em = EnemyManager.Instance;
        string boss = L.T("Boss", "الزعيم");

        _sawHidden = false;
        if (em != null && em.stageBosses != null)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var b in em.stageBosses)
            {
                if (b == null || b.hp <= 0f || !InReach(b.transform.position) || !IsDiscovered(b.transform)) continue;
                float d = (b.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b.transform; }
            }
            if (best != null) return (best, boss, 4f);
        }
        if (_sawHidden) return (null, boss, 0f);

        // Bosses outside the stage-boss list (Big Bob in the Graveyard boss room, the final boss).
        var other = ClosestEnemy(from, e => (e.enemyFlag & (EEnemyFlag.StageBoss | EEnemyFlag.FinalBoss)) != 0 || e.enemyData.enemyName == EEnemy.GhostKing, boss);
        if (other.Item1 != null || _sawHidden) return other;

        var portal = Nearer(from,
            Closest<InteractablePortal>(from, p => !p.done, L.T("Portal", "البوابة")),
            Closest<InteractablePortalFinal>(from, p => !p.done, L.T("Portal", "البوابة")));
        if (portal.Item1 != null || _sawHidden) return portal.Item1 != null ? portal : (null, L.T("Portal", "البوابة"), 0f);

        if (em != null && em.stageBossIsDead) return default;
        var altar = Nearer(from,
            Closest<InteractableBossSpawner>(from, _ => true, L.T("Boss Portal", "بوابة الزعيم")),
            Closest<InteractableBossSpawnerFinal>(from, s => !s.done, L.T("Final boss portal", "بوابة الزعيم الأخير")));
        return altar.Item1 != null ? altar : (null, L.T("Boss Portal", "بوابة الزعيم"), 0f);
    }

    /// <summary>Graveyard pumpkins are pots with their own debug name (the game counts them for the pumpkin quest).</summary>
    private static bool IsPumpkin(InteractablePot p)
    {
        string pumpkin = InteractablePot.debugGraveyardName;
        if (!string.IsNullOrEmpty(pumpkin) && p.GetDebugName() == pumpkin) return true;
        return p.gameObject.name.IndexOf("pumpkin", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsEnemy(EnemyData data, EEnemy which)
    {
        try { return data != null && data.enemyName == which; } catch { return false; }
    }

    private static bool IsMiniboss(Enemy e)
    {
        var f = e.enemyFlag;
        if ((f & (EEnemyFlag.Boss | EEnemyFlag.StageBoss | EEnemyFlag.FinalBoss)) != 0) return false;
        if ((f & (EEnemyFlag.Elite | EEnemyFlag.SummonerMiniboss)) != 0) return true;
        var n = e.enemyData.enemyName;
        return n is EEnemy.MinibossPig or EEnemy.MinibossGolem or EEnemy.MinibossGolemSand or EEnemy.MinibossScorpion;
    }

    private static (Transform, string, float) Closest<T>(Vector3 from, Func<T, bool> wanted, string label) where T : Component
    {
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<T>()))
        {
            var c = o.TryCast<T>();
            if (c == null) continue;
            try { if (!wanted(c)) continue; } catch { continue; }
            if (!InReach(c.transform.position)) continue;
            if (!IsDiscovered(c.transform)) continue;
            float d = (c.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c.transform; }
        }
        return (best, label, 2.5f);
    }

    private static (Transform, string, float) ClosestEnemy(Vector3 from, Func<Enemy, bool> wanted, string label)
    {
        var em = EnemyManager.Instance;
        if (em == null || em.enemies == null) return default;
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (var kv in em.enemies)
        {
            var e = kv.Value;
            if (e == null || e.hp <= 0f || e.enemyData == null) continue;
            try { if (!wanted(e)) continue; } catch { continue; }
            if (!InReach(e.transform.position)) continue;
            if (!IsDiscovered(e.transform)) continue;
            float d = (e.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e.transform; }
        }
        return (best, label, 3f);
    }

    private static (Transform, string, float) Nearer(Vector3 from, (Transform T, string L, float H) a, (Transform T, string L, float H) b)
    {
        if (a.T == null) return b;
        if (b.T == null) return a;
        return (a.T.position - from).sqrMagnitude <= (b.T.position - from).sqrMagnitude ? a : b;
    }

    // ---------------------------------------------------------------- minimap (the game's own edge arrows)

    private static void SyncMinimap()
    {
        if (_minimapBroken) return;
        if (!Plugin.Cfg.GuideMinimap.Value) { ClearMinimap(); return; }

        var mm = MinimapScript();
        if (mm == null) { OurMinimapArrows.Clear(); MinimapTransforms.Clear(); return; }

        var wanted = Targets.Where(t => t.Transform != null).GroupBy(t => t.Transform.Pointer).ToDictionary(g => g.Key, g => g.First());

        foreach (var ptr in OurMinimapArrows.ToList())
        {
            if (wanted.ContainsKey(ptr)) continue;
            if (MinimapTransforms.TryGetValue(ptr, out var old) && old != null && mm.arrowDict.ContainsKey(old)) mm.RemoveArrow(old);
            OurMinimapArrows.Remove(ptr);
            MinimapTransforms.Remove(ptr);
        }

        foreach (var (ptr, t) in wanted)
        {
            if (OurMinimapArrows.Contains(ptr)) continue;
            if (mm.arrowDict.ContainsKey(t.Transform)) continue; // the game already points at it (boss altar, portal)
            mm.AddArrow(t.Transform, t.Color);
            OurMinimapArrows.Add(ptr);
            MinimapTransforms[ptr] = t.Transform;
        }
    }

    private static void ClearMinimap()
    {
        if (OurMinimapArrows.Count == 0) return;
        try
        {
            var mm = MinimapScript();
            if (mm != null)
                foreach (var t in MinimapTransforms.Values)
                    if (t != null && mm.arrowDict.ContainsKey(t)) mm.RemoveArrow(t);
        }
        catch (Exception e) { LogOnce("Could not clear the minimap", e); }
        OurMinimapArrows.Clear();
        MinimapTransforms.Clear();
    }

    private static Assets.Scripts.Camera.MinimapCamera MinimapScript()
    {
        var p = MyPlayer.Instance;
        return p != null ? p.minimapCameraScript : null;
    }

    // ---------------------------------------------------------------- drawing (Hud.Draw, every OnGUI)

    /// <summary>
    /// A compass arrow around the player that turns toward the quest target. It only gives the direction:
    /// no marker on the target and no distance, so the player still has to find the thing.
    /// </summary>
    public static void Draw()
    {
        if (!Visible || !Tracker.InRun || Time.timeScale <= 0f || Picker.Open || KeyBinder.Open || Hud.LayoutMode) return;
        if (Cursor.visible) return; // a game menu (level-up, chest, pause) is open
        if (Event.current.type != EventType.Repaint) return;

        var cam = Camera.main;
        if (cam == null) return;
        EnsureArrow();

        float s = Hud.BaseScale * Mathf.Clamp(Plugin.Cfg.GuideScale.Value, 0.5f, 2.5f);
        var player = PlayerPosition();
        var sp = cam.WorldToScreenPoint(player + Vector3.up);
        if (sp.z < 0f) return;
        var center = new Vector2(sp.x, Screen.height - sp.y); // GUI space: y goes down

        // The camera's flat forward and right, so "up on screen" means "ahead of the camera".
        var fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = cam.transform.up;
        fwd.y = 0f; fwd.Normalize();
        var right = new Vector3(fwd.z, 0f, -fwd.x);

        var saved = GUI.matrix;
        int ring = 0, findLine = 0;
        foreach (var t in Targets)
        {
            if (t.Find)
            {
                // Not found yet: just the objective under the character, with no direction.
                DrawText(center + new Vector2(0f, (70f + findLine++ * 26f) * s), "?  " + t.Label, t.Color, s);
                continue;
            }

            Vector3 pos;
            try
            {
                if (t.Transform == null) continue;
                pos = t.Transform.position;
            }
            catch { continue; }

            var d = pos - player; d.y = 0f;
            if (d.magnitude < 6f) continue; // you're there: let the player look around
            var dir = new Vector2(Vector3.Dot(d, right), -Vector3.Dot(d, fwd)).normalized;

            float radius = (120f + ring * 34f) * s;
            ring++;
            var at = center + dir * radius;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f + ring);
            DrawArrow(at, angle, 30f * s * pulse, t.Color);
            DrawText(at + dir * 30f * s, t.Label, t.Color, s);
        }
        GUI.matrix = saved;

        if (_mapHint != null)
        {
            if (_mapHintUntil < 0f) _mapHintUntil = Time.unscaledTime + 10f; // count from the first frame it's seen
            if (Time.unscaledTime < _mapHintUntil) DrawMapHint(s);
        }
    }

    private static void DrawArrow(Vector2 pivot, float angle, float size, Color color)
    {
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(pivot.x - size / 2f, pivot.y - size / 2f, size, size), _arrow);
        GUI.color = old;
        GUI.matrix = saved;
    }

    private static void DrawText(Vector2 anchor, string text, Color color, float s)
    {
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        var a = anchor / s;
        var style = Hud.TagStyle;
        var size = Hud.Measure(text, style);
        var r = new Rect(a.x - size.x / 2f, a.y - size.y / 2f, size.x, size.y);
        r.x = Mathf.Clamp(r.x, 4f, Screen.width / s - size.x - 4f);
        r.y = Mathf.Clamp(r.y, 4f, Screen.height / s - size.y - 4f);
        Hud.Fill(new Rect(r.x - 4f, r.y - 1f, r.width + 8f, r.height + 2f), new Color(0f, 0f, 0f, 0.5f));
        Hud.Label(r, text, style, color);
        GUI.matrix = saved;
    }

    private static void DrawMapHint(float s)
    {
        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float sw = Screen.width / s;
        var style = Hud.HintStyle;
        float y = 130f;
        foreach (var line in _mapHint.Split('\n'))
        {
            var size = Hud.Measure(line, style);
            var r = new Rect((sw - size.x) / 2f, y, size.x, size.y);
            Hud.Fill(new Rect(r.x - 10f, r.y - 4f, r.width + 20f, r.height + 8f), new Color(0f, 0f, 0f, 0.75f));
            Hud.Label(r, line, style, Hud.GoldColor);
            y += size.y + 12f;
        }
        GUI.matrix = saved;
    }

    /// <summary>A white arrowhead pointing right (tip, two back corners and a notch), with a dark rim.</summary>
    private static void EnsureArrow()
    {
        if (_arrow != null) return;
        const int n = 64;
        var poly = new[] { new Vector2(60f, 32f), new Vector2(8f, 58f), new Vector2(22f, 32f), new Vector2(8f, 6f) };
        _arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        var rim = new Color(0.08f, 0.08f, 0.1f, 1f);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            Color c = new(0f, 0f, 0f, 0f);
            if (Inside(poly, p))
            {
                float edge = float.MaxValue;
                for (int i = 0; i < poly.Length; i++) edge = Mathf.Min(edge, SegmentDistance(p, poly[i], poly[(i + 1) % poly.Length]));
                c = edge < 3.5f ? rim : Color.white;
            }
            _arrow.SetPixel(x, y, c);
        }
        _arrow.Apply();
    }

    private static bool Inside(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    private static void LogOnce(string what, Exception e)
    {
        if (_loggedError) return;
        _loggedError = true;
        Plugin.Logger.LogWarning($"Quest guide: {what}: {e.Message} (further errors hidden)");
    }
}
