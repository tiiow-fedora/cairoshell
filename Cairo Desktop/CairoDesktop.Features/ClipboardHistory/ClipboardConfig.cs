using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CairoDesktop.Features.ClipboardHistory
{
    public sealed class ClipboardConfig
    {
        public static readonly string[] DefaultExcludedApps =
        {
            "KeePass*", "KeePassXC*", "1Password*", "Bitwarden*", "LastPass*", "Dashlane*", "Enpass*", "NordPass*", "RoboForm*", "Keeper*"
        };

        /// <summary>Off by default: recording the clipboard is something the user must choose.</summary>
        public bool Enabled { get; set; }
        public int MaxItems { get; set; } = 100;
        public long MaxBytes { get; set; } = 5L * 1024 * 1024;
        /// <summary>0 = unpinned entries never expire.</summary>
        public int TtlMinutes { get; set; } = 1440;
        /// <summary>Off by default: history stays in memory only.</summary>
        public bool Persist { get; set; }
        public IList<string> ExcludeApps { get; set; } = new List<string>(DefaultExcludedApps);
        public int PinnedMax { get; set; } = 20;
        public string Hotkey { get; set; } = "Ctrl+Alt+V";
        public int PreviewLength { get; set; } = 80;

        public ClipboardStoreOptions ToStoreOptions() => new ClipboardStoreOptions
        {
            MaxItems = MaxItems,
            MaxBytes = MaxBytes,
            MaxPinned = Math.Min(PinnedMax, MaxItems),
            Ttl = TtlMinutes > 0 ? TimeSpan.FromMinutes(TtlMinutes) : (TimeSpan?)null
        };

        public SensitivePolicyOptions ToPolicyOptions() => new SensitivePolicyOptions { ExcludedApps = new List<string>(ExcludeApps) };
    }

    public sealed class ClipboardConfigFinding
    {
        public ClipboardConfigFinding(bool isError, string path, string message)
        {
            IsError = isError;
            Path = path;
            Message = message;
        }

        /// <summary>True when the value was unusable and a default replaced it; false when it was merely adjusted.</summary>
        public bool IsError { get; }
        public string Path { get; }
        public string Message { get; }
        public override string ToString() => Path + ": " + Message;
    }

    public sealed class ClipboardConfigResult
    {
        internal ClipboardConfigResult(ClipboardConfig config, List<ClipboardConfigFinding> findings)
        {
            Config = config;
            Findings = findings;
        }

        public ClipboardConfig Config { get; }
        public IReadOnlyList<ClipboardConfigFinding> Findings { get; }
    }

    /// <summary>Reads the "clipboardHistory" section. Bad values fall back to defaults with a finding; never throws.</summary>
    public static class ClipboardConfigParser
    {
        public const string SectionName = "clipboardHistory";

        private static readonly Regex HotkeyKey = new Regex(@"^([A-Za-z0-9]|F([1-9]|1[0-9]|2[0-4])|Space|Enter|Return|Esc|Escape|Tab|Left|Right|Up|Down|Insert|Delete|Home|End|PageUp|PageDown|Plus|Minus)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] Modifiers = { "Win", "Ctrl", "Alt", "Shift" };

        /// <summary>Parses the full cairo-plus.json text (looks for the section; missing section = defaults, no finding).</summary>
        public static ClipboardConfigResult ParseDocument(string json)
        {
            var findings = new List<ClipboardConfigFinding>();
            if (string.IsNullOrWhiteSpace(json)) return new ClipboardConfigResult(new ClipboardConfig(), findings);
            try
            {
                var docOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using (JsonDocument doc = JsonDocument.Parse(json, docOptions))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(SectionName, out JsonElement section))
                        return ParseSection(section);
                }
            }
            catch (JsonException ex)
            {
                findings.Add(new ClipboardConfigFinding(true, "$", "Config is not valid JSON (" + ex.Message + "); clipboard history uses defaults."));
            }

            return new ClipboardConfigResult(new ClipboardConfig(), findings);
        }

        public static ClipboardConfigResult ParseSection(JsonElement section)
        {
            var cfg = new ClipboardConfig();
            var f = new List<ClipboardConfigFinding>();
            string p = SectionName;

            if (section.ValueKind != JsonValueKind.Object)
            {
                f.Add(new ClipboardConfigFinding(true, p, "Expected an object; using defaults."));
                return new ClipboardConfigResult(cfg, f);
            }

            foreach (JsonProperty prop in section.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "enabled": cfg.Enabled = Bool(prop, cfg.Enabled, f); break;
                    case "persist": cfg.Persist = Bool(prop, cfg.Persist, f); break;
                    case "maxItems": cfg.MaxItems = Int(prop, cfg.MaxItems, 1, 1000, f); break;
                    case "maxBytes": cfg.MaxBytes = Int(prop, (int)Math.Min(int.MaxValue, cfg.MaxBytes), 1024, int.MaxValue, f); break;
                    case "ttlMinutes": cfg.TtlMinutes = Int(prop, cfg.TtlMinutes, 0, 525600, f); break;
                    case "pinnedMax": cfg.PinnedMax = Int(prop, cfg.PinnedMax, 0, 200, f); break;
                    case "previewLength": cfg.PreviewLength = Int(prop, cfg.PreviewLength, 10, 500, f); break;
                    case "hotkey": cfg.Hotkey = Hotkey(prop, cfg.Hotkey, f); break;
                    case "excludeApps": cfg.ExcludeApps = Apps(prop, cfg.ExcludeApps, f); break;
                    default:
                        f.Add(new ClipboardConfigFinding(false, p + "." + prop.Name, "Unknown option ignored."));
                        break;
                }
            }

            if (cfg.PinnedMax > cfg.MaxItems)
            {
                f.Add(new ClipboardConfigFinding(false, p + ".pinnedMax", $"Cannot exceed maxItems ({cfg.MaxItems}); lowered to {cfg.MaxItems}."));
                cfg.PinnedMax = cfg.MaxItems;
            }

            return new ClipboardConfigResult(cfg, f);
        }

        private static bool Bool(JsonProperty prop, bool fallback, List<ClipboardConfigFinding> f)
        {
            if (prop.Value.ValueKind == JsonValueKind.True) return true;
            if (prop.Value.ValueKind == JsonValueKind.False) return false;
            f.Add(new ClipboardConfigFinding(true, SectionName + "." + prop.Name, "Expected true or false; using " + (fallback ? "true" : "false") + "."));
            return fallback;
        }

        private static int Int(JsonProperty prop, int fallback, int min, int max, List<ClipboardConfigFinding> f)
        {
            string path = SectionName + "." + prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d))
            {
                f.Add(new ClipboardConfigFinding(true, path, "Expected a number; using " + fallback + "."));
                return fallback;
            }

            double rounded = Math.Round(d);
            if (rounded < min || rounded > max)
            {
                int clamped = (int)Math.Max(min, Math.Min(max, rounded));
                f.Add(new ClipboardConfigFinding(false, path, $"{d} is outside {min}..{max}; using {clamped}."));
                return clamped;
            }

            return (int)rounded;
        }

        private static string Hotkey(JsonProperty prop, string fallback, List<ClipboardConfigFinding> f)
        {
            string path = SectionName + ".hotkey";
            if (prop.Value.ValueKind == JsonValueKind.Null) return null; // explicit null disables the hotkey
            if (prop.Value.ValueKind == JsonValueKind.String && TryNormalizeHotkey(prop.Value.GetString(), out string norm)) return norm;
            f.Add(new ClipboardConfigFinding(true, path, "Not a valid hotkey (use e.g. \"Ctrl+Alt+V\" with at least one of Win/Ctrl/Alt/Shift); using " + fallback + "."));
            return fallback;
        }

        /// <summary>Validates "Ctrl+Alt+V"-style text and returns it in canonical order (Win, Ctrl, Alt, Shift, key). A bare key is refused.</summary>
        public static bool TryNormalizeHotkey(string text, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+').Select(x => x.Trim()).ToArray();
            if (parts.Length < 2 || parts.Any(x => x.Length == 0)) return false;

            var mods = new HashSet<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string m = parts[i].ToLowerInvariant();
                string canon = m == "win" || m == "super" || m == "meta" ? "Win"
                    : m == "ctrl" || m == "control" ? "Ctrl"
                    : m == "alt" ? "Alt"
                    : m == "shift" ? "Shift" : null;
                if (canon == null || !mods.Add(canon)) return false;
            }

            string key = parts[parts.Length - 1];
            if (!HotkeyKey.IsMatch(key)) return false;
            key = key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant();
            normalized = string.Join("+", Modifiers.Where(mods.Contains).Concat(new[] { key }));
            return true;
        }

        private static IList<string> Apps(JsonProperty prop, IList<string> fallback, List<ClipboardConfigFinding> f)
        {
            string path = SectionName + ".excludeApps";
            if (prop.Value.ValueKind != JsonValueKind.Array)
            {
                f.Add(new ClipboardConfigFinding(true, path, "Expected an array of process-name patterns; using defaults."));
                return fallback;
            }

            var list = new List<string>();
            int i = 0;
            foreach (JsonElement e in prop.Value.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) list.Add(e.GetString().Trim());
                else f.Add(new ClipboardConfigFinding(false, path + "[" + i + "]", "Not a non-empty string; ignored."));
                i++;
            }

            // The user's list REPLACES the default one; [] therefore means "exclude no apps" (the sensitive heuristics still apply).
            return list;
        }
    }
}
