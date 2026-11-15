using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace QuestTracker;

/// <summary>
/// Arabic for Unity's immediate-mode GUI, which only draws left-to-right and never joins letters.
/// Text is shaped (see RtlShaper.cs), wrapped by us in reading order, and each line is reversed into
/// the visual order the GUI then draws as-is.
/// </summary>
internal static partial class Rtl
{
    private static readonly Dictionary<(string, int, int, int), string> Cache = new();

    public static bool HasArabic(string s) => NeedsShaping(s);

    /// <summary>Visual-order text for the GUI. Wraps to <paramref name="width"/> when the style wraps.</summary>
    public static string Visual(string text, GUIStyle style, float width)
    {
        if (!HasArabic(text)) return text;
        bool wrap = style.wordWrap && width > 1f;
        var key = (text, wrap ? (int)width : 0, style.fontSize, (int)style.fontStyle);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Shape once in reading order, then lay out words into lines.
        string shaped = Shape(text);
        var lines = new List<string>();
        foreach (var paragraph in shaped.Split('\n'))
        {
            if (!wrap) { lines.Add(paragraph); continue; }

            var nowrap = new GUIStyle(style) { wordWrap = false };
            var words = paragraph.Split(' ');
            var line = new StringBuilder();
            foreach (var word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && nowrap.CalcSize(new GUIContent(Reverse(candidate))).x > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }
            lines.Add(line.ToString());
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(Reverse(lines[i]));
        }

        if (Cache.Count > 2048) Cache.Clear();
        return Cache[key] = sb.ToString();
    }

    /// <summary>
    /// Shape() already reversed left-to-right runs and mirrored brackets for right-to-left layout,
    /// so reversing the whole line yields left-to-right visual order with numbers/English intact.
    /// Rich-text tags are kept as units.
    /// </summary>
    private static string Reverse(string s)
    {
        var tokens = new List<string>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<')
            {
                int close = s.IndexOf('>', i + 1);
                if (close > i && close - i < 80) { tokens.Add(s.Substring(i, close - i + 1)); i = close; continue; }
            }
            tokens.Add(s[i].ToString());
        }
        tokens.Reverse();
        // Tags were reversed too: swap each opening/closing pair back by re-pairing them in order.
        var sb = new StringBuilder(s.Length);
        foreach (var t in tokens) sb.Append(t.Length > 1 && t.StartsWith("<") ? FlipTag(t) : t);
        return sb.ToString();
    }

    private static string FlipTag(string tag)
    {
        // After reversal, closing tags come first. Turn "</b>" into "<b>" and the original opener into "</b>".
        // Tags with values (e.g. <color=#fff>) can't be rebuilt from their closer, so drop colour/size tags here.
        bool closing = tag.StartsWith("</");
        int start = closing ? 2 : 1;
        int end = tag.IndexOfAny(new[] { '=', ' ', '>' }, start);
        string name = tag.Substring(start, end - start);
        if (name is not ("b" or "i")) return "";
        return closing ? "<" + name + ">" : "</" + name + ">";
    }
}

/// <summary>Quest Tracker's own labels in English or Arabic.</summary>
internal static class L
{
    private static bool _arabic;
    private static int _checkedFrame = -1;
    private static bool _looked;
    private static System.Reflection.PropertyInfo _arabicMode; // MegabonkArabic.Plugin.ArabicOn
    private static bool _alwaysArabic;                         // older Arabic pack: it replaced English

    /// <summary>
    /// Arabic when Megabonk Arabic has the game in Arabic, or when forced in the config.
    /// Older Arabic packs replaced English outright, so with one of those, Arabic is always on.
    /// </summary>
    public static bool Arabic
    {
        get
        {
            string mode = Plugin.Cfg.Language.Value;
            if (mode == "Arabic") return true;
            if (mode == "English") return false;
            if (UnityEngine.Time.frameCount == _checkedFrame) return _arabic;
            _checkedFrame = UnityEngine.Time.frameCount;
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    if (BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.TryGetValue("arabic.megabonk", out var info))
                    {
                        var type = info.Instance?.GetType().Assembly.GetType("MegabonkArabic.Plugin");
                        _arabicMode = type?.GetProperty("ArabicOn", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        _alwaysArabic = _arabicMode == null;
                    }
                }
                _arabic = _alwaysArabic || (_arabicMode != null && (bool)_arabicMode.GetValue(null));
            }
            catch { _arabic = false; }
            return _arabic;
        }
    }

    public static string T(string english, string arabic) => Arabic ? arabic : english;
}
