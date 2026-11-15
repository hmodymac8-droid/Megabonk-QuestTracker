using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace QuestTracker;

/// <summary>
/// Checks Nexus once per game launch for a newer version of this mod and shows a small notice in the menus.
/// The check runs in the background (the game never waits) through Nexus's public API, needs no account or key,
/// and fails silently without internet. Nothing is downloaded or installed: clicking the notice opens the mod page.
/// The same file is used by Quest Tracker and Megabonk Arabic.
/// </summary>
internal sealed class UpdateChecker
{
    private const int MegabonkGameId = 7674; // Nexus game id for Megabonk
    private const string Api = "https://api.nexusmods.com/v2/graphql";

    private readonly string _modName;
    private readonly int _modId;
    private readonly string _current;
    private readonly int _slot;                       // stacks notices from several mods
    private readonly Func<bool> _arabic;
    private readonly Func<string, string> _visual;    // turns Arabic into drawable text
    private volatile string _latest;
    private bool _dismissed;
    private GUIStyle _style, _close;
    private Texture2D _bg;

    public UpdateChecker(string modName, int modId, string currentVersion, int slot, Func<bool> arabic, Func<string, string> visual)
    {
        _modName = modName; _modId = modId; _current = currentVersion; _slot = slot; _arabic = arabic; _visual = visual;
    }

    private string Url => $"https://www.nexusmods.com/megabonk/mods/{_modId}?tab=files";

    public void Start(Action<string> log)
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"{_modName.Replace(" ", "")}/{_current}");
                string query = "{\"query\":\"{ legacyMods(ids: [{gameId: " + MegabonkGameId + ", modId: " + _modId + "}]) { nodes { version } } }\"}";
                using var response = http.PostAsync(Api, new StringContent(query, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var nodes = doc.RootElement.GetProperty("data").GetProperty("legacyMods").GetProperty("nodes");
                if (nodes.GetArrayLength() == 0) return;
                string latest = nodes[0].GetProperty("version").GetString();
                if (IsNewer(latest, _current))
                {
                    _latest = latest;
                    log($"{_modName} {latest} is available on Nexus (you have {_current}).");
                }
            }
            catch (Exception e) { log($"Update check skipped: {e.Message}"); }
        }) { IsBackground = true, Name = "UpdateChecker" };
        thread.Start();
    }

    private static bool IsNewer(string latest, string current)
    {
        static Version Parse(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) v = v.Substring(0, cut);
            return Version.TryParse(v, out var parsed) ? parsed : null;
        }
        var a = Parse(latest); var b = Parse(current);
        return a != null && b != null && a > b;
    }

    /// <summary>Draws the notice. Call from OnGUI; <paramref name="show"/> is false during runs.</summary>
    public void OnGUI(bool show)
    {
        if (_latest == null || _dismissed || !show) return;
        EnsureStyles();

        float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);
        var old = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try
        {
            float sw = Screen.width / scale;
            var box = new Rect(sw - 440f, 90f + _slot * 70f, 420f, 60f);
            bool arabic = _arabic();
            string line1 = arabic ? $"تحديث جديد: {_modName} {_latest}" : $"{_modName} {_latest} is out on Nexus";
            string line2 = arabic ? $"عندك {_current}. اضغط هنا لفتح صفحة المود." : $"You have {_current}. Click here to open the mod page.";

            var e = Event.current;
            var closeRect = new Rect(box.xMax - 30f, box.y + 6f, 24f, 24f);
            if (e.type == EventType.MouseDown && e.button == 0 && box.Contains(e.mousePosition))
            {
                if (closeRect.Contains(e.mousePosition)) _dismissed = true;
                else { Application.OpenURL(Url); _dismissed = true; }
                e.Use();
            }

            GUI.DrawTexture(box, _bg);
            var text = new Rect(box.x + 12f, box.y + 6f, box.width - 50f, 24f);
            _style.alignment = arabic ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            _style.fontStyle = FontStyle.Bold;
            _style.normal.textColor = new Color(1f, 0.82f, 0.25f);
            GUI.Label(text, arabic ? _visual(line1) : line1, _style);
            _style.fontStyle = FontStyle.Normal;
            _style.normal.textColor = Color.white;
            GUI.Label(new Rect(text.x, text.y + 24f, text.width, 24f), arabic ? _visual(line2) : line2, _style);
            GUI.Label(closeRect, "x", _close);
        }
        finally { GUI.matrix = old; }
    }

    private void EnsureStyles()
    {
        if (_style != null && _bg != null) return;
        _bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        _bg.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.92f));
        _bg.Apply();
        _style = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = false, richText = false };
        _close = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        _close.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
    }
}
