using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CairoDesktop.Features.WallpaperSlideshow
{
    /// <summary>The "wallpaperSlideshow" section of cairo-plus.json. Off by default.</summary>
    public sealed class SlideshowConfig
    {
        public static readonly string[] Styles = { "fill", "fit", "stretch", "center", "tile", "span" };

        public bool Enabled { get; set; }
        /// <summary>Folder of pictures. Environment variables like %USERPROFILE% are expanded by the adapter.</summary>
        public string Folder { get; set; }
        public int IntervalMinutes { get; set; } = 30;
        public bool Shuffle { get; set; } = true;
        public bool IncludeSubfolders { get; set; }
        public string Style { get; set; } = "fill";
        /// <summary>Change the wallpaper as soon as Cairo starts, instead of waiting for the interval to run out.</summary>
        public bool ChangeOnStart { get; set; }
    }

    public sealed class SlideshowFinding
    {
        public SlideshowFinding(bool isError, string path, string message)
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

    public sealed class SlideshowConfigResult
    {
        internal SlideshowConfigResult(SlideshowConfig config, List<SlideshowFinding> findings)
        {
            Config = config;
            Findings = findings;
        }

        public SlideshowConfig Config { get; }
        public IReadOnlyList<SlideshowFinding> Findings { get; }
    }

    /// <summary>Reads "wallpaperSlideshow". Never throws; a slideshow that cannot work (no folder) is switched off with an error finding.</summary>
    public static class SlideshowConfigParser
    {
        public const string SectionName = "wallpaperSlideshow";

        public static SlideshowConfigResult ParseDocument(string json)
        {
            var findings = new List<SlideshowFinding>();
            if (string.IsNullOrWhiteSpace(json)) return new SlideshowConfigResult(new SlideshowConfig(), findings);
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
                findings.Add(new SlideshowFinding(true, "$", "Config is not valid JSON (" + ex.Message + "); the wallpaper slideshow stays off."));
            }

            return new SlideshowConfigResult(new SlideshowConfig(), findings);
        }

        public static SlideshowConfigResult ParseSection(JsonElement section)
        {
            var cfg = new SlideshowConfig();
            var f = new List<SlideshowFinding>();
            if (section.ValueKind != JsonValueKind.Object)
            {
                f.Add(new SlideshowFinding(true, SectionName, "Expected an object; the slideshow stays off."));
                return new SlideshowConfigResult(cfg, f);
            }

            foreach (JsonProperty p in section.EnumerateObject())
            {
                string path = SectionName + "." + p.Name;
                switch (p.Name)
                {
                    case "enabled": cfg.Enabled = Bool(p, path, cfg.Enabled, f); break;
                    case "shuffle": cfg.Shuffle = Bool(p, path, cfg.Shuffle, f); break;
                    case "includeSubfolders": cfg.IncludeSubfolders = Bool(p, path, cfg.IncludeSubfolders, f); break;
                    case "changeOnStart": cfg.ChangeOnStart = Bool(p, path, cfg.ChangeOnStart, f); break;
                    case "folder":
                        if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())) cfg.Folder = p.Value.GetString().Trim();
                        else f.Add(new SlideshowFinding(true, path, "Expected a folder path."));
                        break;
                    case "intervalMinutes":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
                        {
                            int v = (int)Math.Max(1, Math.Min(1440, Math.Round(d)));
                            if (v != Math.Round(d)) f.Add(new SlideshowFinding(false, path, d + " is outside 1..1440; using " + v + "."));
                            cfg.IntervalMinutes = v;
                        }
                        else f.Add(new SlideshowFinding(true, path, "Expected a number of minutes; using " + cfg.IntervalMinutes + "."));
                        break;
                    case "style":
                        string style = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString().Trim().ToLowerInvariant() : null;
                        if (style != null && SlideshowConfig.Styles.Contains(style)) cfg.Style = style;
                        else f.Add(new SlideshowFinding(true, path, "Expected one of " + string.Join(", ", SlideshowConfig.Styles) + "; using fill."));
                        break;
                    default: f.Add(new SlideshowFinding(false, path, "Unknown option ignored.")); break;
                }
            }

            if (cfg.Enabled && string.IsNullOrEmpty(cfg.Folder))
            {
                f.Add(new SlideshowFinding(true, SectionName + ".folder", "A folder is required; the slideshow is switched off."));
                cfg.Enabled = false;
            }

            return new SlideshowConfigResult(cfg, f);
        }

        private static bool Bool(JsonProperty p, string path, bool fallback, List<SlideshowFinding> f)
        {
            if (p.Value.ValueKind == JsonValueKind.True) return true;
            if (p.Value.ValueKind == JsonValueKind.False) return false;
            f.Add(new SlideshowFinding(true, path, "Expected true or false; using " + (fallback ? "true" : "false") + "."));
            return fallback;
        }
    }
}
