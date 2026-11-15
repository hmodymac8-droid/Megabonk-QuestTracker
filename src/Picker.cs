using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using UnityEngine;

namespace QuestTracker;

/// <summary>
/// A searchable list of every quest, for tracking quests without going through the game's menus.
/// Megabonk's IL2CPP build is missing parts of Unity's GUI (GUIContent.Temp, which GUI.TextField, GUI.Toggle
/// and the string overloads of GUI.Button use), so the search box, checkbox and scrolling are hand-made
/// from labels, rectangles and GUIContent buttons.
/// </summary>
internal static class Picker
{
    public static bool Open { get; private set; }
    public static bool SearchFocused => Open && _searchFocused;

    private sealed class Entry
    {
        public string Id, Name, Desc, Type, Progress;
        public MyAchievement Ach;
        public bool Done;
    }

    private const float RowH = 62f;
    private const float ScrollbarW = 10f;
    private const int MaxSearchLength = 40;

    private static List<Entry> _entries = new();
    private static string[] _tabs = { "All" };
    private static string _tab = "All";
    private static string _search = "";
    private static bool _searchFocused;
    private static bool _showDone;
    private static int _scrollRow;
    private static bool _draggingScrollbar;

    public static void Toggle()
    {
        Open = !Open;
        _searchFocused = false;
        _draggingScrollbar = false;
        if (Open) Refresh();
    }

    private static void Refresh()
    {
        Tracker.LoadAll();
        var list = new List<Entry>();
        foreach (var a in Tracker.All.Values)
        {
            try
            {
                if (a.IsHiddenInMenus()) continue;
                list.Add(new Entry
                {
                    Id = a.internalName,
                    Ach = a,
                    Name = Strip(a.GetDisplayName()),
                    Desc = Strip(a.GetUnlockRequirement()),
                    Type = a.achievementType.ToString(),
                    Done = a.IsCompleted(),
                    Progress = a.IsUsingTargetValue() && a.targetValue > 1 ? $"{Math.Max(0, a.GetCurrentValue()):N0} / {a.targetValue:N0}" : "",
                });
            }
            catch { /* skip quests that fail to read */ }
        }
        _entries = list.OrderBy(e => e.Done).ThenBy(e => e.Type).ThenBy(e => e.Name).ToList();
        _tabs = new[] { "All", "Tracked" }.Concat(_entries.Select(e => e.Type).Distinct()).ToArray();
        _scrollRow = 0;
    }

    /// <param name="mouse">Mouse position in this window's (scaled) GUI coordinates.</param>
    public static void Draw(float sw, float sh, Vector2 mouse)
    {
        var e = Event.current;
        HandleTyping(e);

        float w = Mathf.Min(680f, sw - 40f), h = Mathf.Min(660f, sh - 40f);
        var r = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
        Hud.Fill(r, new Color(0.04f, 0.04f, 0.07f, 0.96f));
        Hud.Fill(new Rect(r.x, r.y, r.width, 4f), Hud.GoldColor);

        float x = r.x + 16f, y = r.y + 14f, iw = r.width - 32f;
        Hud.Label(new Rect(x, y, iw - 300f, 28f), L.T($"Quest Tracker  ·  {Tracker.Pinned.Count}/{Plugin.Cfg.MaxPinned.Value} tracked", $"متتبع المهام  ·  {Tracker.Pinned.Count}/{Plugin.Cfg.MaxPinned.Value} متتبعة"), Hud.Header, Hud.GoldColor);
        if (Hud.Btn(new Rect(r.xMax - 276f, r.y + 12f, 74f, 28f), L.T("Keys", "المفاتيح"), Hud.Button)) { Toggle(); KeyBinder.Show(); return; }
        if (Hud.Btn(new Rect(r.xMax - 196f, r.y + 12f, 140f, 28f), L.T("Move / Resize", "تحريك / تحجيم"), Hud.Button)) { Hud.SetLayoutMode(true); return; }
        if (Hud.Btn(new Rect(r.xMax - 48f, r.y + 12f, 34f, 28f), "X", Hud.Button)) { Toggle(); return; }
        y += 38f;

        // --- search box (hand-made) ---
        var box = new Rect(x, y, iw - 210f, 30f);
        DrawSearchBox(box, e, mouse);

        // --- "show completed" checkbox (hand-made) ---
        var check = new Rect(r.xMax - 190f, y + 3f, 174f, 24f);
        if (Hud.Btn(check, (_showDone ? "[x]" : "[  ]") + L.T("  Show completed", "  إظهار المكتملة"), Hud.Tab)) { _showDone = !_showDone; _scrollRow = 0; }
        y += 40f;

        // --- category tabs ---
        float tx = x;
        foreach (var t in _tabs)
        {
            float tw = Hud.Measure(TabName(t), Hud.Tab).x + 18f;
            if (tx + tw > x + iw) break;
            if (Hud.Btn(new Rect(tx, y, tw, 26f), TabName(t), t == _tab ? Hud.TabOn : Hud.Tab)) { _tab = t; _scrollRow = 0; }
            tx += tw + 4f;
        }
        y += 34f;

        // --- list with manual scrolling ---
        var shown = Filtered().ToList();
        var view = new Rect(x, y, iw, r.yMax - y - 36f);
        int visible = Mathf.Max(1, (int)(view.height / RowH));
        int maxRow = Mathf.Max(0, shown.Count - visible);
        HandleScrolling(e, mouse, view, visible, maxRow, shown.Count);
        _scrollRow = Mathf.Clamp(_scrollRow, 0, maxRow);

        float rowW = view.width - ScrollbarW - 8f;
        for (int i = 0; i < visible && _scrollRow + i < shown.Count; i++)
            DrawRow(shown[_scrollRow + i], new Rect(view.x, view.y + i * RowH, rowW, RowH - 4f));

        if (shown.Count == 0)
            Hud.Label(new Rect(view.x + 10f, view.y + 10f, view.width - 20f, 24f),
                _search.Length > 0 ? L.T($"No quests match \"{_search}\"", $"لا توجد مهام تطابق \"{_search}\"") : L.T("No quests here", "لا توجد مهام هنا"), Hud.RowDesc, Hud.GreyColor);

        if (maxRow > 0) DrawScrollbar(ScrollbarRect(view), visible, shown.Count);

        Hud.Label(new Rect(x, r.yMax - 30f, iw, 22f),
            L.T($"{shown.Count} quests  ·  Scroll with the mouse wheel  ·  Tip: hover any quest or locked skin in the game's menus and press [{Plugin.Cfg.PinKey.Value}]  ·  [Esc] close",
              $"{shown.Count} مهمة  ·  استخدم عجلة الفأرة للتمرير  ·  نصيحة: مرر فوق أي مهمة أو مظهر مقفل في قوائم اللعبة واضغط [{Plugin.Cfg.PinKey.Value}]  ·  [Esc] إغلاق"),
            Hud.RowDesc, Hud.GreyColor);
    }

    // ---------------------------------------------------------------- search box

    private static void DrawSearchBox(Rect box, Event e, Vector2 mouse)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            var clear = new Rect(box.xMax - 28f, box.y, 28f, box.height);
            if (_search.Length > 0 && clear.Contains(mouse)) { _search = ""; _scrollRow = 0; _searchFocused = true; e.Use(); }
            else if (box.Contains(mouse)) { _searchFocused = true; e.Use(); }
            else _searchFocused = false;
        }

        Hud.Fill(box, new Color(1f, 1f, 1f, _searchFocused ? 0.12f : 0.07f));
        Hud.Outline(box, _searchFocused ? Hud.GoldColor : new Color(1f, 1f, 1f, 0.25f), 1f);

        var textRect = new Rect(box.x + 8f, box.y + 5f, box.width - 44f, box.height - 8f);
        if (_search.Length == 0 && !_searchFocused)
        {
            Hud.Label(textRect, L.T("Click here to search quests...", "انقر هنا للبحث عن مهمة..."), Hud.RowDesc, Hud.GreyColor);
            return;
        }

        bool caretOn = _searchFocused && (int)(Time.unscaledTime * 2f) % 2 == 0;
        Hud.Label(textRect, _search + (caretOn ? "|" : ""), Hud.Field, Color.white);

        if (_search.Length > 0)
            Hud.Label(new Rect(box.xMax - 22f, box.y + 5f, 18f, 20f), "x", Hud.RowTitle, Hud.GreyColor);
    }

    private static void HandleTyping(Event e)
    {
        if (!_searchFocused || e.type != EventType.KeyDown) return;

        switch (e.keyCode)
        {
            case KeyCode.Backspace:
                if (_search.Length > 0) { _search = _search.Substring(0, _search.Length - 1); _scrollRow = 0; }
                e.Use();
                return;
            case KeyCode.Escape:
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                _searchFocused = false;
                e.Use();
                return;
        }

        char c = e.character;
        if (c != '\0' && !char.IsControl(c))
        {
            if (_search.Length < MaxSearchLength) { _search += c; _scrollRow = 0; }
            e.Use();
        }
    }

    // ---------------------------------------------------------------- scrolling

    private static Rect ScrollbarRect(Rect view) => new(view.xMax - ScrollbarW, view.y, ScrollbarW, view.height);

    private static void HandleScrolling(Event e, Vector2 mouse, Rect view, int visible, int maxRow, int total)
    {
        if (e.type == EventType.ScrollWheel && view.Contains(mouse))
        {
            _scrollRow += e.delta.y > 0 ? 2 : -2;
            e.Use();
            return;
        }

        if (maxRow == 0) return;
        var bar = ScrollbarRect(view);
        var grab = new Rect(bar.x - 6f, bar.y, bar.width + 12f, bar.height); // a bit wider so it's easy to grab

        if (e.type == EventType.MouseDown && e.button == 0 && grab.Contains(mouse)) _draggingScrollbar = true;
        if (e.type == EventType.MouseUp) _draggingScrollbar = false;

        if (_draggingScrollbar && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            float t = Mathf.InverseLerp(bar.y, bar.yMax, mouse.y);
            _scrollRow = Mathf.RoundToInt(t * maxRow);
            e.Use();
        }
    }

    private static void DrawScrollbar(Rect bar, int visible, int total)
    {
        Hud.Fill(bar, new Color(1f, 1f, 1f, 0.08f));
        float thumbH = Mathf.Max(30f, bar.height * visible / total);
        float maxRow = Mathf.Max(1, total - visible);
        float thumbY = bar.y + (bar.height - thumbH) * (_scrollRow / maxRow);
        Hud.Fill(new Rect(bar.x, thumbY, bar.width, thumbH), _draggingScrollbar ? Hud.GoldColor : new Color(1f, 1f, 1f, 0.45f));
    }

    // ---------------------------------------------------------------- rows

    private static IEnumerable<Entry> Filtered()
    {
        string s = (_search ?? "").Trim();
        foreach (var e in _entries)
        {
            bool pinned = Tracker.IsPinned(e.Id);
            if (_tab == "Tracked" && !pinned) continue;
            if (_tab != "All" && _tab != "Tracked" && e.Type != _tab) continue;
            if (e.Done && !_showDone && !pinned) continue;
            if (s.Length > 0 && e.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0
                              && e.Desc.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0) continue;
            yield return e;
        }
    }

    private static void DrawRow(Entry e, Rect r)
    {
        bool pinned = Tracker.IsPinned(e.Id);
        Hud.Fill(r, pinned ? new Color(1f, 0.82f, 0.25f, 0.14f) : new Color(1f, 1f, 1f, 0.04f));

        float bw = 96f;
        string title = e.Name + (e.Done ? L.T("  <size=12>(completed)</size>", "  (مكتملة)") : "") + (e.Progress.Length > 0 ? $"   <size=12>({e.Progress})</size>" : "");
        Hud.Label(new Rect(r.x + 10f, r.y + 5f, r.width - bw - 24f, 22f), title, Hud.RowTitle, e.Done ? Hud.GreenColor : Color.white);
        Hud.Label(new Rect(r.x + 10f, r.y + 27f, r.width - bw - 24f, r.height - 28f), e.Desc, Hud.RowDesc, Hud.GreyColor);

        if (Hud.Btn(new Rect(r.xMax - bw - 8f, r.y + (r.height - 30f) / 2f, bw, 30f), pinned ? L.T("Untrack", "إلغاء") : L.T("Track", "تتبع"), Hud.Button))
        {
            Tracker.TogglePin(e.Ach);
            Hud.Toast(pinned ? L.T("Quest untracked", "تم إلغاء تتبع المهمة") : L.T("Quest tracked!", "تم تتبع المهمة!"));
        }
    }

    /// <summary>Display name for a tab key (tab keys stay English internally).</summary>
    private static string TabName(string tab) => !L.Arabic ? tab : tab switch
    {
        "All" => "الكل",
        "Tracked" => "المتتبعة",
        "General" => "عام",
        "Characters" => "الشخصيات",
        "Weapons" => "الأسلحة",
        "Challenges" => "التحديات",
        "Items" => "الأغراض",
        "Tomes" => "الكتب",
        "Skins" => "المظاهر",
        "Hats" => "القبعات",
        _ => tab,
    };

    private static string Strip(string s) =>
        string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "").Trim();
}
