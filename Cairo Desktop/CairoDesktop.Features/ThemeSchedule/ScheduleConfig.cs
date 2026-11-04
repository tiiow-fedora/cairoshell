using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace CairoDesktop.Features.ThemeSchedule
{
    public enum ScheduleMode { Fixed, Sun }

    /// <summary>The "themeSchedule" section of cairo-plus.json. Off by default.</summary>
    public sealed class ScheduleConfig
    {
        public bool Enabled { get; set; }
        public ScheduleMode Mode { get; set; } = ScheduleMode.Fixed;
        /// <summary>Fixed mode: local time the light version starts.</summary>
        public TimeSpan LightFrom { get; set; } = TimeSpan.FromHours(7);
        /// <summary>Fixed mode: local time the dark version starts.</summary>
        public TimeSpan DarkFrom { get; set; } = TimeSpan.FromHours(19);
        /// <summary>Sun mode: degrees north; required.</summary>
        public double? Latitude { get; set; }
        /// <summary>Sun mode: degrees east; required.</summary>
        public double? Longitude { get; set; }
        /// <summary>Sun mode: minutes added to sunrise (negative = earlier).</summary>
        public int SunriseOffsetMinutes { get; set; }
        /// <summary>Sun mode: minutes added to sunset.</summary>
        public int SunsetOffsetMinutes { get; set; }
    }

    public sealed class ScheduleFinding
    {
        public ScheduleFinding(bool isError, string path, string message)
        {
            IsError = isError;
            Path = path;
            Message = message;
        }

        public bool IsError { get; }
        public string Path { get; }
        public string Message { get; }
        public override string ToString() => Path + ": " + Message;
    }

    public sealed class ScheduleConfigResult
    {
        internal ScheduleConfigResult(ScheduleConfig config, List<ScheduleFinding> findings)
        {
            Config = config;
            Findings = findings;
        }

        public ScheduleConfig Config { get; }
        public IReadOnlyList<ScheduleFinding> Findings { get; }
    }

    /// <summary>Reads "themeSchedule". Bad values give a finding and a safe fallback; a schedule that cannot work is switched off. Never throws.</summary>
    public static class ScheduleConfigParser
    {
        public const string SectionName = "themeSchedule";

        public static ScheduleConfigResult ParseDocument(string json)
        {
            var findings = new List<ScheduleFinding>();
            if (string.IsNullOrWhiteSpace(json)) return new ScheduleConfigResult(new ScheduleConfig(), findings);
            try
            {
                var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using (JsonDocument doc = JsonDocument.Parse(json, opts))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(SectionName, out JsonElement section))
                        return ParseSection(section);
                }
            }
            catch (JsonException ex)
            {
                findings.Add(new ScheduleFinding(true, "$", "Config is not valid JSON (" + ex.Message + "); the theme schedule stays off."));
            }

            return new ScheduleConfigResult(new ScheduleConfig(), findings);
        }

        public static ScheduleConfigResult ParseSection(JsonElement section)
        {
            var cfg = new ScheduleConfig();
            var f = new List<ScheduleFinding>();
            if (section.ValueKind != JsonValueKind.Object)
            {
                f.Add(new ScheduleFinding(true, SectionName, "Expected an object; the schedule stays off."));
                return new ScheduleConfigResult(cfg, f);
            }

            foreach (JsonProperty p in section.EnumerateObject())
            {
                string path = SectionName + "." + p.Name;
                switch (p.Name)
                {
                    case "enabled":
                        if (p.Value.ValueKind == JsonValueKind.True) cfg.Enabled = true;
                        else if (p.Value.ValueKind == JsonValueKind.False) cfg.Enabled = false;
                        else f.Add(new ScheduleFinding(true, path, "Expected true or false; using false."));
                        break;
                    case "mode":
                        string mode = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString().Trim().ToLowerInvariant() : null;
                        if (mode == "fixed") cfg.Mode = ScheduleMode.Fixed;
                        else if (mode == "sun") cfg.Mode = ScheduleMode.Sun;
                        else f.Add(new ScheduleFinding(true, path, "Expected \"fixed\" or \"sun\"; using fixed."));
                        break;
                    case "lightFrom": cfg.LightFrom = Time(p, path, cfg.LightFrom, f); break;
                    case "darkFrom": cfg.DarkFrom = Time(p, path, cfg.DarkFrom, f); break;
                    case "latitude": cfg.Latitude = Coordinate(p, path, 90, f); break;
                    case "longitude": cfg.Longitude = Coordinate(p, path, 180, f); break;
                    case "sunriseOffsetMinutes": cfg.SunriseOffsetMinutes = Offset(p, path, f); break;
                    case "sunsetOffsetMinutes": cfg.SunsetOffsetMinutes = Offset(p, path, f); break;
                    default: f.Add(new ScheduleFinding(false, path, "Unknown option ignored.")); break;
                }
            }

            if (cfg.Mode == ScheduleMode.Fixed && cfg.LightFrom == cfg.DarkFrom)
            {
                f.Add(new ScheduleFinding(true, SectionName, "lightFrom and darkFrom are the same time; using 07:00 and 19:00."));
                cfg.LightFrom = TimeSpan.FromHours(7);
                cfg.DarkFrom = TimeSpan.FromHours(19);
            }

            if (cfg.Mode == ScheduleMode.Sun && (cfg.Latitude == null || cfg.Longitude == null) && cfg.Enabled)
            {
                f.Add(new ScheduleFinding(true, SectionName, "Sun mode needs latitude and longitude; the schedule is switched off."));
                cfg.Enabled = false;
            }

            return new ScheduleConfigResult(cfg, f);
        }

        /// <summary>Accepts "7:30", "07:30", "19:00". Anything else (24:00, "7pm", seconds) is refused.</summary>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h) || parts[0].Length > 2) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m) || parts[1].Length != 2) return false;
            if (h > 23 || m > 59) return false;
            time = new TimeSpan(h, m, 0);
            return true;
        }

        private static TimeSpan Time(JsonProperty p, string path, TimeSpan fallback, List<ScheduleFinding> f)
        {
            if (p.Value.ValueKind == JsonValueKind.String && TryParseTime(p.Value.GetString(), out TimeSpan t)) return t;
            f.Add(new ScheduleFinding(true, path, "Expected a 24-hour time like \"07:30\"; using " + fallback.ToString(@"hh\:mm") + "."));
            return fallback;
        }

        private static double? Coordinate(JsonProperty p, string path, double limit, List<ScheduleFinding> f)
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) <= limit) return d;
            f.Add(new ScheduleFinding(true, path, "Expected a number between -" + limit + " and " + limit + "; value ignored."));
            return null;
        }

        private static int Offset(JsonProperty p, string path, List<ScheduleFinding> f)
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
            {
                double r = Math.Round(d);
                int clamped = (int)Math.Max(-180, Math.Min(180, r));
                if (clamped != r) f.Add(new ScheduleFinding(false, path, d + " is outside -180..180; using " + clamped + "."));
                return clamped;
            }

            f.Add(new ScheduleFinding(true, path, "Expected a number of minutes; using 0."));
            return 0;
        }
    }
}
