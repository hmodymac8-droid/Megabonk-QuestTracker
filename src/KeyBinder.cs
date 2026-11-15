using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace QuestTracker;

/// <summary>
/// In-game window for changing Quest Tracker's keys: click an action, then press the new key.
/// Keys the game itself uses are refused, and two of our actions never share a key (they swap instead).
/// </summary>
internal static class KeyBinder
{
    public static bool Open { get; private set; }

    private static ConfigEntry<KeyCode> _listening;
    private static string _message;
    private static Color _messageColor;

    // Megabonk's default controls (Settings > Controls), plus keys every player needs.
    private static readonly HashSet<KeyCode> GameKeys = new()
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Space, KeyCode.E, KeyCode.LeftControl, KeyCode.R, KeyCode.Tab,
        KeyCode.Escape, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Backslash,
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2,
    };

    private static IEnumerable<(ConfigEntry<KeyCode> Entry, string Label)> Actions()
    {
        var c = Plugin.Cfg;
        yield return (c.PinKey, L.T("Track the hovered quest", "تتبع المهمة تحت المؤشر"));
        yield return (c.PickerKey, L.T("Open the quest list", "فتح قائمة المهام"));
        yield return (c.HudKey, L.T("Show / hide the tracker", "إظهار / إخفاء المتتبع"));
        yield return (c.GuideKey, L.T("Show / hide the quest guide", "إظهار / إخفاء دليل المهام"));
        yield return (c.LayoutKey, L.T("Move / resize the tracker", "تحريك / تحجيم المتتبع"));
        yield return (c.DumpKey, L.T("Export the quest list", "تصدير قائمة المهام"));
    }

    public static void Show()
    {
        Open = true;
        _listening = null;
        _message = null;
    }

    private static void Close(bool backToList)
    {
        Open = false;
        _listening = null;
        Plugin.Cfg.Save();
        if (backToList && !Picker.Open) Picker.Toggle();
    }

    /// <summary>Gets every key press while the window is open.</summary>
    public static void HandleKey(Event e)
    {
        if (_listening == null)
        {
            if (e.keyCode == KeyCode.Escape) Close(backToList: true);
            return;
        }

        var key = e.keyCode;
        e.Use();
        if (key == KeyCode.Escape) { _listening = null; _message = null; return; }

        if (GameKeys.Contains(key))
        {
            Say(L.T($"[{key}] is used by the game. Pick another key.", $"[{key}] مستخدم في اللعبة. اختر مفتاحا آخر."), Hud.RedColor);
            return;
        }

        // Another of our actions already has this key: give it ours, so nothing is ever bound twice.
        var other = Actions().Select(a => a.Entry).FirstOrDefault(a => a != _listening && a.Value == key);
        if (other != null)
        {
            other.Value = _listening.Value;
            Say(L.T($"Swapped: the other action now uses [{other.Value}].", $"تم التبديل: الإجراء الآخر يستخدم الآن [{other.Value}]."), Hud.GoldColor);
        }
        else Say(L.T($"Saved: [{key}]", $"تم الحفظ: [{key}]"), Hud.GreenColor);

        _listening.Value = key;
        _listening = null;
        Plugin.Cfg.Save();
    }

    private static void Say(string text, Color color)
    {
        _message = text;
        _messageColor = color;
    }

    /// <param name="mouse">Mouse position in this window's (scaled) GUI coordinates.</param>
    public static void Draw(float sw, float sh, Vector2 mouse)
    {
        var actions = Actions().ToList();
        float w = Mathf.Min(560f, sw - 40f), h = 150f + actions.Count * 40f;
        var r = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
        Hud.Fill(r, new Color(0.04f, 0.04f, 0.07f, 0.96f));
        Hud.Fill(new Rect(r.x, r.y, r.width, 4f), Hud.GoldColor);

        float x = r.x + 16f, y = r.y + 14f, iw = r.width - 32f;
        Hud.Label(new Rect(x, y, iw - 60f, 28f), L.T("Quest Tracker  ·  Keys", "متتبع المهام  ·  المفاتيح"), Hud.Header, Hud.GoldColor);
        if (Hud.Btn(new Rect(r.xMax - 48f, r.y + 12f, 34f, 28f), "X", Hud.Button)) { Close(backToList: false); return; }
        y += 40f;

        Hud.Label(new Rect(x, y, iw, 20f), L.T("Click an action, then press the new key.  [Esc] cancels.", "انقر على إجراء ثم اضغط المفتاح الجديد.  [Esc] للإلغاء."), Hud.RowDesc, Hud.GreyColor);
        y += 28f;

        foreach (var (entry, label) in actions)
        {
            var row = new Rect(x, y, iw, 34f);
            bool listening = _listening == entry;
            Hud.Fill(row, listening ? new Color(1f, 0.82f, 0.25f, 0.16f) : new Color(1f, 1f, 1f, 0.04f));
            Hud.Label(new Rect(row.x + 10f, row.y + 7f, iw - 190f, 22f), label, Hud.RowTitle, Color.white);

            string keyText = listening ? L.T("Press a key...", "اضغط مفتاحا...") : $"[ {entry.Value} ]";
            if (Hud.Btn(new Rect(row.xMax - 170f, row.y + 2f, 160f, 30f), keyText, listening ? Hud.TabOn : Hud.Button))
            {
                _listening = listening ? null : entry;
                _message = null;
            }
            y += 40f;
        }

        if (_message != null) Hud.Label(new Rect(x, y + 2f, iw, 22f), _message, Hud.RowDesc, _messageColor);

        var reset = new Rect(x, r.yMax - 42f, 170f, 30f);
        if (Hud.Btn(reset, L.T("Reset to defaults", "استعادة الافتراضي"), Hud.Button))
        {
            foreach (var (entry, _) in actions) entry.Value = (KeyCode)entry.DefaultValue;
            _listening = null;
            Plugin.Cfg.Save();
            Say(L.T("All keys reset to defaults", "تمت استعادة كل المفاتيح الافتراضية"), Hud.GreenColor);
        }
        if (Hud.Btn(new Rect(r.xMax - 16f - 170f, r.yMax - 42f, 170f, 30f), L.T("Back to quest list", "العودة إلى القائمة"), Hud.Button)) Close(backToList: true);
    }
}
