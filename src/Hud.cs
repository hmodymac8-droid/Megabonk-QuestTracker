using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using UnityEngine;

namespace QuestTracker;

/// <summary>Draws the tracker panel, layout mode, hints and toasts with Unity's immediate-mode GUI.</summary>
internal static class Hud
{
    // Sizes at 1080p with Size = 1. Everything scales with resolution and the Size setting.
    private const float PanelWidth = 400f;
    private const float Pad = 12f;
    private const float TitleRow = 26f;
    private const float StatusRow = 24f;
    private const float BarRow = 16f;
    private const float Handle = 22f;
    private const float RemoveSize = 22f;

    private static bool _stylesReady;
    private static GUIStyle _title, _desc, _status, _timer, _hint, _tag, _header, _rowTitle, _rowDesc, _button, _tab, _tabOn, _field;
    private static Texture2D _white;

    private static readonly Color Gold = new(1f, 0.82f, 0.25f);
    private static readonly Color Green = new(0.35f, 0.95f, 0.45f);
    private static readonly Color Yellow = new(1f, 0.85f, 0.2f);
    private static readonly Color Red = new(1f, 0.33f, 0.3f);
    private static readonly Color Grey = new(0.72f, 0.72f, 0.78f);
    private static readonly Color Blue = new(0.45f, 0.78f, 1f);

    private static string _toast;
    private static float _toastUntil;
    private static Color _toastColor = new(1f, 0.82f, 0.25f);
    private static bool _mouseHeld;

    // Layout mode (F8): drag to move, scroll / drag corner to resize.
    public static bool LayoutMode { get; private set; }
    private enum Drag { None, Move, Resize }
    private static Drag _drag;
    private static Vector2 _dragStart;
    private static float _startX, _startY, _startScale;

    private static readonly TrackedQuest Sample = new()
    {
        Id = "_sample", Title = L.T("Example quest", "مهمة تجريبية"), Description = L.T("Kill 15,000 enemies as Ogre", "اقتل 15,000 من الأعداء بشخصية الغول"),
        HasCount = true, Current = 11162, Target = 15000, Progress = 0.744f,
        Status = QuestStatus.InProgress, StatusText = L.T("3,838 left  ·  +312 this run", "متبقي 3,838  ·  +312 في هذه الجولة"),
    };

    /// <summary>Resolution scale: 1 at 1080p.</summary>
    internal static float BaseScale => Mathf.Clamp(Screen.height / 1080f, 0.6f, 3f);
    private static float PanelScale => BaseScale * Mathf.Clamp(Plugin.Cfg.Scale.Value, 0.5f, 2.5f);

    // ---------------------------------------------------------------- entry point

    public static void Draw()
    {
        try
        {
            EnsureStyles();
            var e = Event.current;
            var cfg = Plugin.Cfg;
            var rawMouse = e.mousePosition; // screen pixels: no GUI matrix is applied yet

            HandleKeys(e);
            UiHoverScanner.Scan(e);

            // While our windows are open the game gets no input. After closing, wait for the mouse button
            // to come up so the click that closed the window can't land in the game.
            if (e.type == EventType.MouseDown) _mouseHeld = true;
            else if (e.type == EventType.MouseUp) _mouseHeld = false;
            bool ourWindowOpen = Picker.Open || LayoutMode || KeyBinder.Open;
            InputBlocker.Update(ourWindowOpen || (InputBlocker.Blocking && _mouseHeld));
            if (ourWindowOpen) CursorGuard.Hold();
            else if (!InputBlocker.Blocking) CursorGuard.Release();

            var cards = PanelCards();
            bool showPanel = LayoutMode || (cfg.ShowHud.Value && cards.Count > 0 && (Tracker.InRun || cfg.ShowInMenus.Value));

            Guide.Draw(); // under the panel and windows

            var saved = GUI.matrix;
            Rect? hoveredPanel = null;
            if (showPanel)
            {
                float s = PanelScale;
                var heights = cards.Select(q => CardHeight(q, PanelWidth - Pad * 2)).ToList();
                float h = Pad + heights.Sum(x => x + Pad);
                var screenRect = PanelScreenRect(s, h);

                if (LayoutMode) HandleLayoutMouse(e, screenRect, s, cards, heights);
                screenRect = PanelScreenRect(s, h); // may have moved
                if (!LayoutMode && !Picker.Open && Cursor.visible && screenRect.Contains(e.mousePosition)) hoveredPanel = screenRect;

                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                DrawPanel(cards, heights, screenRect.x / s, screenRect.y / s, h);
                GUI.matrix = saved;
            }

            float b = BaseScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(b, b, 1f));
            float sw = Screen.width / b, sh = Screen.height / b;
            if (LayoutMode) DrawLayoutHelp(sw);
            if (hoveredPanel.HasValue) DrawPanelTag(hoveredPanel.Value, b, sh);
            if (Tracker.HoveredId != null && !Picker.Open && !LayoutMode) DrawHoverHint(rawMouse / b);
            if (Picker.Open) Picker.Draw(sw, sh, rawMouse / b);
            if (KeyBinder.Open) KeyBinder.Draw(sw, sh, rawMouse / b);
            if (_toast != null && Time.unscaledTime < _toastUntil) DrawToast(sw);
            GUI.matrix = saved;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Quest Tracker draw error: {ex.Message}");
        }
    }

    /// <summary>Layout mode shows every tracked quest (so they can be removed); otherwise failed ones fade out.</summary>
    private static List<TrackedQuest> PanelCards()
    {
        if (LayoutMode) return Tracker.Pinned.Count == 0 ? new List<TrackedQuest> { Sample } : Tracker.Pinned.ToList();
        return Tracker.Pinned.Where(q => !Tracker.IsHidden(q)).ToList();
    }

    /// <summary>Card rectangles in panel units (before scaling), in the same order as the cards.</summary>
    private static List<Rect> CardRects(float x, float y, List<float> heights)
    {
        var rects = new List<Rect>();
        float cy = y + Pad;
        foreach (var h in heights)
        {
            rects.Add(new Rect(x + Pad + 4f, cy, PanelWidth - Pad * 2 - 4f, h));
            cy += h + Pad;
        }
        return rects;
    }

    private static Rect RemoveButton(Rect card) => new(card.xMax - RemoveSize, card.y + 2f, RemoveSize, RemoveSize);

    private static Rect PanelScreenRect(float s, float h)
    {
        var cfg = Plugin.Cfg;
        float w = PanelWidth * s, hh = h * s;
        float x = Mathf.Clamp(cfg.PosX.Value * Screen.width, 0f, Mathf.Max(0f, Screen.width - w));
        float y = Mathf.Clamp(cfg.PosY.Value * Screen.height, 0f, Mathf.Max(0f, Screen.height - hh));
        return new Rect(x, y, w, hh);
    }

    // ---------------------------------------------------------------- keys + layout mode

    private static void HandleKeys(Event e)
    {
        if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
        var cfg = Plugin.Cfg;

        // The key window gets every key: it's either waiting for a new binding or closing on Esc.
        if (KeyBinder.Open) { KeyBinder.HandleKey(e); return; }

        // While typing in the search box, every key belongs to the search box (Esc just leaves the box).
        if (Picker.SearchFocused && e.keyCode != cfg.PickerKey.Value) return;

        if (LayoutMode)
        {
            if (e.keyCode == KeyCode.Escape || e.keyCode == cfg.LayoutKey.Value || e.keyCode == KeyCode.Return) SetLayoutMode(false);
            else if (e.keyCode == KeyCode.R) ResetLayout();
            else if (e.keyCode is KeyCode.Equals or KeyCode.KeypadPlus or KeyCode.Plus) Resize(+0.1f);
            else if (e.keyCode is KeyCode.Minus or KeyCode.KeypadMinus) Resize(-0.1f);
            return;
        }

        if (e.keyCode == cfg.PinKey.Value && Tracker.HoveredId != null)
        {
            bool was = Tracker.IsPinned(Tracker.HoveredId);
            Tracker.TogglePinHovered();
            Toast(was ? L.T("Quest untracked", "تم إلغاء تتبع المهمة") : L.T("Quest tracked!", "تم تتبع المهمة!"));
        }
        else if (e.keyCode == cfg.PickerKey.Value) Picker.Toggle();
        else if (e.keyCode == KeyCode.Escape && Picker.Open) Picker.Toggle();
        else if (e.keyCode == cfg.LayoutKey.Value) SetLayoutMode(true);
        else if (e.keyCode == cfg.HudKey.Value)
        {
            cfg.ShowHud.Value = !cfg.ShowHud.Value;
            cfg.Save();
            Toast(cfg.ShowHud.Value ? L.T("Quest tracker shown", "تم إظهار متتبع المهام") : L.T("Quest tracker hidden", "تم إخفاء متتبع المهام"));
        }
        else if (e.keyCode == cfg.GuideKey.Value)
        {
            cfg.ShowGuide.Value = !cfg.ShowGuide.Value;
            cfg.Save();
            Toast(cfg.ShowGuide.Value ? L.T("Quest guide shown", "تم إظهار دليل المهام") : L.T("Quest guide hidden", "تم إخفاء دليل المهام"));
        }
        else if (e.keyCode == cfg.DumpKey.Value)
        {
            try { Toast(L.T("Quest list saved to ", "تم حفظ قائمة المهام في ") + System.IO.Path.GetFileName(Tracker.ExportAll())); }
            catch (Exception ex) { Toast(L.T("Export failed: ", "فشل التصدير: ") + ex.Message); }
        }
    }

    public static void SetLayoutMode(bool on)
    {
        LayoutMode = on;
        _drag = Drag.None;
        if (on)
        {
            if (Picker.Open) Picker.Toggle();
            Plugin.Cfg.ShowHud.Value = true;
        }
        else
        {
            Plugin.Cfg.Save();
            Toast(L.T("Tracker position saved", "تم حفظ موضع المتتبع"));
        }
    }

    private static void ResetLayout()
    {
        var cfg = Plugin.Cfg;
        cfg.PosX.Value = Settings.DefaultPosX;
        cfg.PosY.Value = Settings.DefaultPosY;
        cfg.Scale.Value = Settings.DefaultScale;
        Toast(L.T("Tracker reset to default", "تمت إعادة المتتبع للوضع الافتراضي"));
    }

    private static void Resize(float delta)
    {
        var cfg = Plugin.Cfg;
        cfg.Scale.Value = Mathf.Clamp((float)Math.Round(cfg.Scale.Value + delta, 2), 0.5f, 2.5f);
    }

    private static void HandleLayoutMouse(Event e, Rect r, float s, List<TrackedQuest> cards, List<float> heights)
    {
        var cfg = Plugin.Cfg;
        // Mouse positions here are in screen pixels (no GUI matrix applied yet).
        var mp = e.mousePosition;
        var handle = new Rect(r.xMax - Handle * s, r.yMax - Handle * s, Handle * s, Handle * s);

        // Untrack buttons come first so clicking them doesn't start a drag.
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            var rects = CardRects(r.x / s, r.y / s, heights);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == Sample) continue;
                var b = RemoveButton(rects[i]);
                var screenBtn = new Rect(b.x * s, b.y * s, b.width * s, b.height * s);
                if (!screenBtn.Contains(mp)) continue;
                Tracker.Untrack(cards[i]);
                Toast(L.T("Quest untracked", "تم إلغاء تتبع المهمة"));
                e.Use();
                return;
            }
        }

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && r.Contains(mp):
                _drag = handle.Contains(mp) ? Drag.Resize : Drag.Move;
                _dragStart = mp;
                _startX = r.x;
                _startY = r.y;
                _startScale = cfg.Scale.Value;
                e.Use();
                break;

            case EventType.MouseDrag when _drag != Drag.None:
                var d = mp - _dragStart;
                if (_drag == Drag.Move)
                {
                    cfg.PosX.Value = Mathf.Clamp01((_startX + d.x) / Screen.width);
                    cfg.PosY.Value = Mathf.Clamp01((_startY + d.y) / Screen.height);
                }
                else
                {
                    float startW = PanelWidth * BaseScale * _startScale;
                    cfg.Scale.Value = Mathf.Clamp(_startScale * (startW + d.x) / startW, 0.5f, 2.5f);
                }
                e.Use();
                break;

            case EventType.MouseUp when _drag != Drag.None:
                _drag = Drag.None;
                e.Use();
                break;

            case EventType.ScrollWheel:
                Resize(e.delta.y > 0 ? -0.05f : 0.05f);
                e.Use();
                break;
        }
    }

    public static void Toast(string text) => Toast(text, Gold, 2.5f);

    /// <summary>A message at the top of the screen; also used for the guide's warnings (longer, red when urgent).</summary>
    public static void Toast(string text, Color color, float seconds)
    {
        _toast = text;
        _toastColor = color;
        _toastUntil = Time.unscaledTime + seconds;
    }

    // ---------------------------------------------------------------- tracker panel

    private static void DrawPanel(List<TrackedQuest> cards, List<float> heights, float x, float y, float total)
    {
        var cfg = Plugin.Cfg;

        Fill(new Rect(x, y, PanelWidth, total), new Color(0.05f, 0.05f, 0.08f, Mathf.Max(cfg.Opacity.Value, LayoutMode ? 0.85f : 0f)));
        Fill(new Rect(x, y, 4f, total), Gold);

        if (LayoutMode)
        {
            Outline(new Rect(x, y, PanelWidth, total), Gold, 2f);
            var handle = new Rect(x + PanelWidth - Handle, y + total - Handle, Handle, Handle);
            Fill(handle, Gold);
            Fill(new Rect(handle.x + 5f, handle.yMax - 8f, Handle - 10f, 3f), Color.black);
            Fill(new Rect(handle.xMax - 8f, handle.y + 5f, 3f, Handle - 10f), Color.black);
        }

        var rects = CardRects(x, y, heights);
        for (int i = 0; i < cards.Count; i++)
        {
            var q = cards[i];
            bool hidden = LayoutMode && Tracker.IsHidden(q);
            if (hidden) Fill(rects[i], new Color(1f, 1f, 1f, 0.06f)); // dimmed: normally hidden (failed/completed)
            DrawCard(q, rects[i]);

            if (LayoutMode && q != Sample)
            {
                var b = RemoveButton(rects[i]);
                Fill(b, Red);
                Fill(new Rect(b.x + 5f, b.center.y - 1.5f, b.width - 10f, 3f), Color.white); // "minus" = remove
            }
        }
    }

    /// <summary>The guide's "how to finish it" line for hidden or multi-step quests, while the guide is on.</summary>
    private static string HowTo(TrackedQuest q)
    {
        if (!Guide.Visible || q == Sample || q.Status is QuestStatus.Complete or QuestStatus.Success) return null;
        string how = GuideTargets.For(q)?.HowTo;
        return how == null ? null : L.T("How: ", "الطريقة: ") + how;
    }

    private static float CardHeight(TrackedQuest q, float w)
    {
        float h = TitleRow;
        h += TextHeight(q.Description, _desc, w - 4f);
        string how = HowTo(q);
        if (how != null) h += TextHeight(how, _desc, w - 4f) + 2f;
        if (ShowsBar(q)) h += BarRow;
        h += StatusRow;
        return h;
    }

    private static bool ShowsBar(TrackedQuest q) => q.HasCount || q.TimerFraction >= 0f;

    private static void DrawCard(TrackedQuest q, Rect r)
    {
        if (Time.unscaledTime < q.FlashUntil)
        {
            float pulse = 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 10f);
            Fill(new Rect(r.x - 6, r.y - 5, r.width + 12, r.height + 10), new Color(Green.r, Green.g, Green.b, pulse));
        }

        var color = StatusColor(q);
        float y = r.y;
        string timer = q.TimerText;
        float timerW = string.IsNullOrEmpty(timer) ? 0f : 70f;
        float rightGap = LayoutMode && q != Sample ? RemoveSize + 6f : 0f; // room for the untrack button

        // Status marker: a small coloured square (plain shapes render in every font).
        Fill(new Rect(r.x, y + 8f, 10f, 10f), color);
        Label(new Rect(r.x + 16f, y, r.width - 16f - timerW - rightGap, TitleRow), q.Title, _title, Gold);
        if (timerW > 0f) Label(new Rect(r.xMax - rightGap - timerW, y, timerW, TitleRow), timer, _timer, color);
        y += TitleRow;

        float dh = TextHeight(q.Description, _desc, r.width);
        Label(new Rect(r.x, y, r.width, dh), q.Description, _desc, Color.white);
        y += dh;

        string how = HowTo(q);
        if (how != null)
        {
            float hh = TextHeight(how, _desc, r.width);
            Label(new Rect(r.x, y + 2f, r.width, hh), how, _desc, Blue);
            y += hh + 2f;
        }

        if (ShowsBar(q))
        {
            float frac = q.TimerFraction >= 0f ? q.TimerFraction : q.Progress;
            var bar = new Rect(r.x, y + 4f, r.width, 8f);
            Fill(bar, new Color(1f, 1f, 1f, 0.15f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), color);
            y += BarRow;
        }

        string prefix = q.Status switch
        {
            QuestStatus.Complete or QuestStatus.Success => L.T("DONE  ", "تم  "),
            QuestStatus.Failed => "",
            _ => q.HasCount && q.TimerFraction < 0f ? $"{q.Current:N0} / {q.Target:N0}   " : "",
        };
        Label(new Rect(r.x, y, r.width, StatusRow), prefix + q.StatusText, _status, color);
    }

    private static Color StatusColor(TrackedQuest q) => q.Status switch
    {
        QuestStatus.Complete or QuestStatus.Success => Green,
        QuestStatus.Failed => Red,
        QuestStatus.Hurry => q.TimerFraction < 0.1f ? Red : Yellow,
        QuestStatus.OnTrack => Green,
        QuestStatus.Idle or QuestStatus.NotThisStage => Grey,
        _ => Blue,
    };

    // ---------------------------------------------------------------- hints, help, toast

    private static void DrawLayoutHelp(float sw)
    {
        string text = L.T("MOVE / RESIZE TRACKER   ·   Drag the panel to move it   ·   Scroll or drag the corner to resize   ·   Red button: untrack   ·   [R] reset   ·   [Esc] done",
            "تحريك وتغيير حجم المتتبع   ·   اسحب اللوحة لتحريكها   ·   استخدم العجلة أو اسحب الزاوية لتغيير الحجم   ·   الزر الأحمر: إلغاء التتبع   ·   [R] إعادة ضبط   ·   [Esc] تم");
        var size = Measure(text, _hint);
        var r = new Rect((sw - size.x) / 2f - 14f, 20f, size.x + 28f, size.y + 14f);
        Fill(r, new Color(0f, 0f, 0f, 0.9f));
        Outline(r, Gold, 2f);
        Label(new Rect(r.x + 14f, r.y + 7f, size.x, size.y), text, _hint, Gold);
    }

    /// <summary>Small tag on the panel's edge while the mouse is over it, so players discover the layout key.</summary>
    private static void DrawPanelTag(Rect panelScreen, float b, float sh)
    {
        string text = L.T($"Press [{Plugin.Cfg.LayoutKey.Value}] to move / resize / remove", $"اضغط [{Plugin.Cfg.LayoutKey.Value}] للتحريك / تغيير الحجم / الإزالة");
        var size = Measure(text, _tag);
        var tag = new Vector2(size.x + 14f, size.y + 6f);

        float x = panelScreen.x / b;
        float y = panelScreen.y / b - tag.y - 2f;               // just above the panel
        if (y < 0f) y = panelScreen.yMax / b + 2f;               // or below it if there's no room
        y = Mathf.Min(y, sh - tag.y);

        var r = new Rect(x, y, tag.x, tag.y);
        Fill(r, new Color(0f, 0f, 0f, 0.85f));
        Fill(new Rect(r.x, r.y, 3f, r.height), Gold);
        Label(new Rect(r.x + 8f, r.y + 3f, size.x, size.y), text, _tag, Gold);
    }

    private static void DrawHoverHint(Vector2 mouse)
    {
        bool pinned = Tracker.IsPinned(Tracker.HoveredId);
        string action = pinned ? L.T("Untrack quest", "إلغاء تتبع المهمة") : L.T("Track quest", "تتبع المهمة");
        string keys = Tracker.HoveredKind == HoverKind.Clickable && Plugin.Cfg.ClickToTrack.Value
            ? L.T($"[Double-click] or [{Plugin.Cfg.PinKey.Value}]", $"[نقرتان] أو [{Plugin.Cfg.PinKey.Value}]")
            : $"[{Plugin.Cfg.PinKey.Value}]";
        string text = $"{keys} {action}";
        var size = Measure(text, _hint);
        var r = new Rect(mouse.x + 20f, mouse.y + 20f, size.x + 18f, size.y + 10f);
        Fill(r, new Color(0f, 0f, 0f, 0.88f));
        Outline(r, pinned ? Grey : Gold, 1f);
        Label(new Rect(r.x + 9f, r.y + 5f, size.x, size.y), text, _hint, pinned ? Grey : Gold);
    }

    private static void DrawToast(float sw)
    {
        var size = Measure(_toast, _hint);
        var r = new Rect((sw - size.x) / 2f - 14f, 80f, size.x + 28f, size.y + 14f);
        Fill(r, new Color(0f, 0f, 0f, 0.88f));
        Label(new Rect(r.x + 14f, r.y + 7f, size.x, size.y), _toast, _hint, _toastColor);
    }

    // ---------------------------------------------------------------- drawing helpers

    internal static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _white);
        GUI.color = old;
    }

    internal static void Outline(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>
    /// A button that avoids GUI.Button(Rect, string, ...): in Megabonk's build those overloads call
    /// GUIContent.Temp, which is missing ("Method unstripping failed"). The GUIContent overload is safe.
    /// </summary>
    internal static bool Btn(Rect r, string text, GUIStyle style) =>
        GUI.Button(r, new GUIContent(Rtl.Visual(text ?? "", style, 0f)), style);

    /// <summary>Draws a label; Arabic is shaped, wrapped in reading order and right-aligned.</summary>
    internal static void Label(Rect r, string text, GUIStyle style, Color c)
    {
        text ??= "";
        if (Rtl.HasArabic(text))
        {
            text = Rtl.Visual(text, style, r.width);
            style = RightAligned(style);
        }
        var old = GUI.contentColor;
        GUI.contentColor = c;
        GUI.Label(r, text, style);
        GUI.contentColor = old;
    }

    /// <summary>Size of a single-line label, measuring Arabic in its drawn form.</summary>
    internal static Vector2 Measure(string text, GUIStyle style) =>
        style.CalcSize(new GUIContent(Rtl.Visual(text ?? "", style, 0f)));

    /// <summary>Height of wrapped text, measuring Arabic the way Label will draw it.</summary>
    internal static float TextHeight(string text, GUIStyle style, float width)
    {
        text ??= "";
        if (!Rtl.HasArabic(text)) return style.CalcHeight(new GUIContent(text), width);
        return RightAligned(style).CalcHeight(new GUIContent(Rtl.Visual(text, style, width)), width);
    }

    private static readonly Dictionary<IntPtr, GUIStyle> RightStyles = new();

    /// <summary>A copy of the style aligned right and without wrapping (Arabic is pre-wrapped).</summary>
    private static GUIStyle RightAligned(GUIStyle s)
    {
        if (RightStyles.TryGetValue(s.Pointer, out var right)) return right;
        right = new GUIStyle(s) { wordWrap = false };
        right.alignment = s.alignment switch
        {
            TextAnchor.UpperLeft => TextAnchor.UpperRight,
            TextAnchor.MiddleLeft => TextAnchor.MiddleRight,
            TextAnchor.LowerLeft => TextAnchor.LowerRight,
            _ => s.alignment,
        };
        return RightStyles[s.Pointer] = right;
    }

    internal static GUIStyle HintStyle => _hint;
    internal static GUIStyle TagStyle => _tag;
    internal static GUIStyle Header => _header;
    internal static GUIStyle RowTitle => _rowTitle;
    internal static GUIStyle RowDesc => _rowDesc;
    internal static GUIStyle Button => _button;
    internal static GUIStyle Tab => _tab;
    internal static GUIStyle TabOn => _tabOn;
    internal static GUIStyle Field => _field;
    internal static Color GoldColor => Gold;
    internal static Color GreenColor => Green;
    internal static Color GreyColor => Grey;
    internal static Color RedColor => Red;

    private static void EnsureStyles()
    {
        if (_stylesReady && _white != null) return;

        _white = new Texture2D(1, 1);
        _white.SetPixel(0, 0, Color.white);
        _white.Apply();
        _white.hideFlags = HideFlags.HideAndDontSave;

        _title = Make(GUI.skin.label, 19, FontStyle.Bold);
        _desc = Make(GUI.skin.label, 15, FontStyle.Normal, wrap: true);
        _status = Make(GUI.skin.label, 15, FontStyle.Bold);
        _timer = Make(GUI.skin.label, 19, FontStyle.Bold);
        _timer.alignment = TextAnchor.MiddleRight;
        _hint = Make(GUI.skin.label, 15, FontStyle.Bold);
        _tag = Make(GUI.skin.label, 12, FontStyle.Bold);
        _header = Make(GUI.skin.label, 20, FontStyle.Bold);
        _rowTitle = Make(GUI.skin.label, 16, FontStyle.Bold);
        _rowDesc = Make(GUI.skin.label, 13, FontStyle.Normal, wrap: true);
        _button = Make(GUI.skin.button, 14, FontStyle.Bold);
        _tab = Make(GUI.skin.button, 13, FontStyle.Normal);
        _tabOn = Make(GUI.skin.button, 13, FontStyle.Bold);
        _tabOn.normal.textColor = Gold;
        _field = Make(GUI.skin.textField, 15, FontStyle.Normal);

        _stylesReady = true;
    }

    private static GUIStyle Make(GUIStyle baseStyle, int size, FontStyle fs, bool wrap = false)
    {
        var s = new GUIStyle(baseStyle) { fontSize = size, fontStyle = fs, wordWrap = wrap, richText = true };
        s.normal.textColor = Color.white;
        return s;
    }
}

/// <summary>Frees the mouse while our own windows are open, then hands it back to the game.</summary>
internal static class CursorGuard
{
    private static bool _held;
    private static CursorLockMode _lock;
    private static bool _visible;

    public static void Hold()
    {
        if (!_held)
        {
            _lock = Cursor.lockState;
            _visible = Cursor.visible;
            _held = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void Release()
    {
        if (!_held) return;
        _held = false;

        // Mid-run (not paused): give the mouse straight back to the game.
        if (Tracker.InRun && Time.timeScale > 0f)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }
        Cursor.lockState = _lock;
        Cursor.visible = _visible;
    }
}



