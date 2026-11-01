using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControllerFusion.Engine;

public static class DiagnosticReport
{
    // Exports contain a status snapshot, never device candidates or raw HID IDs.
    public static string Redact(string text)
    {
        text = Regex.Replace(text, @"(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}", "[Bluetooth address]");
        text = Regex.Replace(text, @"(?i)\b(?:HID|USB|BTHENUM|BTHLEDEVICE)[\\#][^\s\""<>]+", "[device identifier]");
        text = Regex.Replace(text, @"(?i)[A-Z]:\\Users\\[^\\\s\""<>]+", "[user profile]");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? text : text.Replace(home, "[user profile]", StringComparison.OrdinalIgnoreCase);
    }

    public static string Create(ControllerEngine engine, Preferences settings, IEnumerable<string> logs)
    {
        var s = engine.HasInput ? engine.LastFrame : null;
        return JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            AppVersion = typeof(DiagnosticReport).Assembly.GetName().Version?.ToString(),
            CapturedAt = DateTimeOffset.Now,
            Windows = Environment.OSVersion.Version.ToString(),
            Runtime = Environment.Version.ToString(),
            Selection = new { settings.Controller, settings.Transport, settings.Output },
            Status = new { engine.Status.Phase, Detail = Redact(engine.Status.Detail), engine.HasInput, engine.HasOutput },
            Input = new { engine.Reports, engine.Hertz, Battery = engine.HasInput ? engine.Battery : (int?)null, Charging = engine.HasInput ? engine.Charging : (bool?)null },
            engine.FeedbackReports,
            engine.ActiveTransport,
            engine.InputStatistics,
            engine.OutputStatistics,
            Haptics = Redact(engine.HapticsStatus),
            Hiding = Redact(engine.HidingStatus),
            Buttons = s == null ? null : new { s.PS, Create = s.Share, s.Cross, s.Circle, s.Square, s.Triangle, ZL = s.L2, ZR = s.R2, GL = s.BLP, GR = s.BRP, C = s.Switch2RawInputStatus.CButton },
            Logs = logs.TakeLast(80).Select(Redact).ToArray(),
            Note = "This is a diagnostic snapshot. No hardware fault or game compatibility verdict is inferred."
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) });
    }
}
