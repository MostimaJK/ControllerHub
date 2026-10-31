using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace ControllerFusion;

/// <summary>Presentation-only translations. Protocol values, navigation IDs and mappings stay canonical.</summary>
public static class UiText
{
    public static readonly string[] Languages = { "zh-Hant", "zh-Hans", "en" };
    public static readonly string[] LanguageNames = { "繁體中文", "简体中文", "English" };
    static readonly Dictionary<string, string> english = Load("en");
    static readonly Dictionary<string, string> simplified = Load("zh-Hans");
    static readonly Dictionary<string, string> characters = Load("zh-Hans-characters");
    static readonly Dictionary<char, string[]> phrases = english.Keys
        .GroupBy(k => k[0]).ToDictionary(g => g.Key, g => g.OrderByDescending(k => k.Length).ToArray());
    public static string Current { get; private set; } = "zh-Hant";
    public static string Normalize(string? language) => Languages.Contains(language) ? language! : "zh-Hant";
    public static void Use(string? language) => Current = Normalize(language);
    public static FontFamily EnglishFont { get; } = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Google Sans");
    public static FontFamily Font => Current == "en" ? EnglishFont : new FontFamily(Current == "zh-Hans"
        ? "Segoe UI, Microsoft YaHei UI" : "Segoe UI, Microsoft JhengHei UI");

    static Dictionary<string, string> Load(string name)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"ControllerHub.Localization.{name}.json")
            ?? throw new InvalidDataException($"Missing UI language resource: {name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException(name);
    }

    public static string Get(string text)
    {
        if (Current == "zh-Hant" || string.IsNullOrEmpty(text) || !text.Any(c => c >= '\u3400' && c <= '\u9fff')) return text;
        var catalog = Current == "en" ? english : simplified;
        if (catalog.TryGetValue(text, out var translated)) return translated;
        // Dynamic status lines combine translated phrases with measurements and
        // device names. One pass prevents replacement output being translated again.
        var result = new StringBuilder(text.Length);
        for (int at = 0; at < text.Length;)
        {
            string? match = null;
            if (phrases.TryGetValue(text[at], out var candidates))
                foreach (var phrase in candidates)
                    if (text.AsSpan(at).StartsWith(phrase, StringComparison.Ordinal)) { match = phrase; break; }
            if (match != null) { result.Append(catalog[match]); at += match.Length; }
            else { var c = text[at++].ToString(); result.Append(Current == "zh-Hans" && characters.TryGetValue(c, out var value) ? value : c); }
        }
        return result.ToString();
    }
}
