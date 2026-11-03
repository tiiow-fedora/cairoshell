using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace CairoDesktop.Features.SnapLayouts
{
    public enum FindingSeverity { Warning, Error }

    /// <summary>A problem found while reading the config. Findings never stop the rest of the config from being used.</summary>
    public sealed class SnapFinding
    {
        public SnapFinding(FindingSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? "";
            Message = message ?? "";
        }

        public FindingSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString() => $"{Severity.ToString().ToLowerInvariant()}: {Path}: {Message}";
    }

    /// <summary>Settings of the "snapLayouts" section of cairo-plus.json.</summary>
    public sealed class SnapConfig
    {
        public static readonly string[] ActionNames = { "snapLeft", "snapRight", "snapUp", "snapDown", "nextZone", "prevZone", "restore", "cycleLayout" };

        /// <summary>Off unless the user turns it on, like every other Cairo Plus feature.</summary>
        public bool Enabled { get; set; }
        public int Gap { get; set; }
        public int OuterGap { get; set; }
        /// <summary>Layout id used on every monitor without a more specific rule; null means "pick by monitor shape".</summary>
        public string DefaultLayout { get; set; }
        /// <summary>Next/previous zone navigation wraps around at the ends.</summary>
        public bool Wrap { get; set; } = true;
        /// <summary>Maximized windows are restored and snapped instead of being ignored.</summary>
        public bool SnapMaximized { get; set; }
        public List<SnapLayout> CustomLayouts { get; } = new List<SnapLayout>();
        /// <summary>action name -> hotkey string. Missing actions use the defaults; an empty string disables an action.</summary>
        public Dictionary<string, string> Hotkeys { get; } = DefaultHotkeys();
        /// <summary>monitor device name / "WIDTHxHEIGHT" / "primary" -> layout id.</summary>
        public Dictionary<string, string> MonitorLayouts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, string> DefaultHotkeys() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["snapLeft"] = "Win+Alt+Left",
            ["snapRight"] = "Win+Alt+Right",
            ["snapUp"] = "Win+Alt+Up",
            ["snapDown"] = "Win+Alt+Down",
            ["nextZone"] = "Win+Alt+PageDown",
            ["prevZone"] = "Win+Alt+PageUp",
            ["restore"] = "Win+Alt+Backspace",
            ["cycleLayout"] = "Win+Alt+Space"
        };

        public LayoutCatalog BuildCatalog() => new LayoutCatalog(CustomLayouts);
    }

    public sealed class SnapParseResult
    {
        public SnapConfig Config { get; } = new SnapConfig();
        public List<SnapFinding> Findings { get; } = new List<SnapFinding>();
        public bool HasErrors
        {
            get
            {
                foreach (SnapFinding f in Findings) if (f.Severity == FindingSeverity.Error) return true;
                return false;
            }
        }
    }

    /// <summary>Lenient reader for the "snapLayouts" section: comments and trailing commas are allowed, nothing throws.</summary>
    public static class SnapConfigParser
    {
        public const int MaxGap = 200;

        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>Parses a whole cairo-plus.json and reads its "snapLayouts" property (absent = defaults, no findings).</summary>
        public static SnapParseResult ParseDocument(string json)
        {
            var result = new SnapParseResult();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return result;
                using (JsonDocument doc = JsonDocument.Parse(json, Lenient))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        result.Findings.Add(new SnapFinding(FindingSeverity.Error, "$", "the config must be a JSON object."));
                        return result;
                    }

                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "snapLayouts", StringComparison.OrdinalIgnoreCase))
                        {
                            ReadSection(p.Value, result);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Findings.Add(new SnapFinding(FindingSeverity.Error, "$", "could not read the config: " + ex.Message));
            }

            return result;
        }

        /// <summary>Parses just the contents of the "snapLayouts" object.</summary>
        public static SnapParseResult ParseSection(string json)
        {
            var result = new SnapParseResult();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return result;
                using (JsonDocument doc = JsonDocument.Parse(json, Lenient))
                {
                    ReadSection(doc.RootElement, result);
                }
            }
            catch (Exception ex)
            {
                result.Findings.Add(new SnapFinding(FindingSeverity.Error, "snapLayouts", "could not read the section: " + ex.Message));
            }

            return result;
        }

        private static void ReadSection(JsonElement root, SnapParseResult result)
        {
            const string P = "snapLayouts";
            SnapConfig c = result.Config;
            var f = result.Findings;

            if (root.ValueKind != JsonValueKind.Object)
            {
                f.Add(new SnapFinding(FindingSeverity.Error, P, "must be an object."));
                return;
            }

            foreach (JsonProperty prop in root.EnumerateObject())
            {
                string path = P + "." + prop.Name;
                switch (prop.Name.ToLowerInvariant())
                {
                    case "enabled": c.Enabled = ReadBool(prop.Value, path, c.Enabled, f); break;
                    case "wrap": c.Wrap = ReadBool(prop.Value, path, c.Wrap, f); break;
                    case "snapmaximized": c.SnapMaximized = ReadBool(prop.Value, path, c.SnapMaximized, f); break;
                    case "gap": c.Gap = ReadGap(prop.Value, path, f); break;
                    case "outergap": c.OuterGap = ReadGap(prop.Value, path, f); break;
                    case "defaultlayout": c.DefaultLayout = ReadString(prop.Value, path, f); break;
                    case "layouts": ReadLayouts(prop.Value, path, c, f); break;
                    case "hotkeys": ReadHotkeys(prop.Value, path, c, f); break;
                    case "monitors": ReadMonitors(prop.Value, path, c, f); break;
                    default: f.Add(new SnapFinding(FindingSeverity.Warning, path, "unknown setting, ignored.")); break;
                }
            }

            if (!string.IsNullOrWhiteSpace(c.DefaultLayout) && c.BuildCatalog().Find(c.DefaultLayout) == null)
            {
                f.Add(new SnapFinding(FindingSeverity.Warning, P + ".defaultLayout", $"no layout '{c.DefaultLayout}'; the layout is chosen by monitor shape instead."));
                c.DefaultLayout = null;
            }

            LayoutCatalog catalog = c.BuildCatalog();
            var unknown = new List<string>();
            foreach (KeyValuePair<string, string> m in c.MonitorLayouts)
            {
                if (catalog.Find(m.Value) == null) unknown.Add(m.Key);
            }

            foreach (string key in unknown)
            {
                f.Add(new SnapFinding(FindingSeverity.Warning, P + ".monitors." + key, $"no layout '{c.MonitorLayouts[key]}'; this entry is ignored."));
                c.MonitorLayouts.Remove(key);
            }
        }

        private static bool ReadBool(JsonElement v, string path, bool fallback, List<SnapFinding> f)
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected true or false."));
            return fallback;
        }

        private static int ReadGap(JsonElement v, string path, List<SnapFinding> f)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
            {
                if (d < 0)
                {
                    f.Add(new SnapFinding(FindingSeverity.Warning, path, "negative gap, using 0."));
                    return 0;
                }

                if (d > MaxGap)
                {
                    f.Add(new SnapFinding(FindingSeverity.Warning, path, $"gap above {MaxGap} pixels, using {MaxGap}."));
                    return MaxGap;
                }

                return (int)Math.Round(d);
            }

            f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected a number of pixels, using 0."));
            return 0;
        }

        private static string ReadString(JsonElement v, string path, List<SnapFinding> f)
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind != JsonValueKind.Null) f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected a string."));
            return null;
        }

        private static void ReadLayouts(JsonElement v, string path, SnapConfig c, List<SnapFinding> f)
        {
            if (v.ValueKind != JsonValueKind.Array)
            {
                f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected an array of layouts."));
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SnapLayout built in BuiltInLayouts.All()) seen.Add(built.Id);

            int index = 0;
            foreach (JsonElement item in v.EnumerateArray())
            {
                string p = $"{path}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, p, "a layout must be an object."));
                    continue;
                }

                string id = null, name = null;
                bool overlap = false;
                var zones = new List<Zone>();
                bool zonesOk = true;

                foreach (JsonProperty lp in item.EnumerateObject())
                {
                    switch (lp.Name.ToLowerInvariant())
                    {
                        case "id": id = lp.Value.ValueKind == JsonValueKind.String ? lp.Value.GetString() : null; break;
                        case "name": name = lp.Value.ValueKind == JsonValueKind.String ? lp.Value.GetString() : null; break;
                        case "allowoverlap": overlap = lp.Value.ValueKind == JsonValueKind.True; break;
                        case "zones": zonesOk = ReadZones(lp.Value, p + ".zones", zones, f); break;
                        default: f.Add(new SnapFinding(FindingSeverity.Warning, p + "." + lp.Name, "unknown setting, ignored.")); break;
                    }
                }

                if (!zonesOk) continue;

                var layout = new SnapLayout(id, name, zones, overlap);
                IReadOnlyList<string> problems = layout.Validate();
                if (problems.Count > 0)
                {
                    foreach (string problem in problems) f.Add(new SnapFinding(FindingSeverity.Error, p, problem + " Layout skipped."));
                    continue;
                }

                if (!seen.Add(layout.Id))
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, p, $"layout id '{layout.Id}' is already used (built-in or earlier layout). Layout skipped."));
                    continue;
                }

                c.CustomLayouts.Add(layout);
            }
        }

        private static bool ReadZones(JsonElement v, string path, List<Zone> zones, List<SnapFinding> f)
        {
            if (v.ValueKind != JsonValueKind.Array)
            {
                f.Add(new SnapFinding(FindingSeverity.Error, path, "expected an array of zones. Layout skipped."));
                return false;
            }

            int index = 0;
            bool ok = true;
            foreach (JsonElement z in v.EnumerateArray())
            {
                string p = $"{path}[{index++}]";
                if (z.ValueKind != JsonValueKind.Object)
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, p, "a zone must be an object with x, y, w, h. Layout skipped."));
                    ok = false;
                    continue;
                }

                double x = double.NaN, y = double.NaN, w = double.NaN, h = double.NaN;
                foreach (JsonProperty zp in z.EnumerateObject())
                {
                    double val = zp.Value.ValueKind == JsonValueKind.Number && zp.Value.TryGetDouble(out double d) ? d : double.NaN;
                    switch (zp.Name.ToLowerInvariant())
                    {
                        case "x": x = val; break;
                        case "y": y = val; break;
                        case "w": case "width": w = val; break;
                        case "h": case "height": h = val; break;
                    }
                }

                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h))
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, p, "a zone needs numeric x, y, w and h (fractions 0..1). Layout skipped."));
                    ok = false;
                    continue;
                }

                zones.Add(new Zone(x, y, w, h));
            }

            return ok;
        }

        private static void ReadHotkeys(JsonElement v, string path, SnapConfig c, List<SnapFinding> f)
        {
            if (v.ValueKind != JsonValueKind.Object)
            {
                f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected an object mapping action names to hotkeys."));
                return;
            }

            foreach (JsonProperty hp in v.EnumerateObject())
            {
                string action = Array.Find(SnapConfig.ActionNames, a => string.Equals(a, hp.Name, StringComparison.OrdinalIgnoreCase));
                string p = path + "." + hp.Name;
                if (action == null)
                {
                    f.Add(new SnapFinding(FindingSeverity.Warning, p, "unknown action (use " + string.Join(", ", SnapConfig.ActionNames) + ")."));
                    continue;
                }

                if (hp.Value.ValueKind == JsonValueKind.Null || (hp.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(hp.Value.GetString())))
                {
                    c.Hotkeys[action] = ""; // explicitly disabled
                    continue;
                }

                if (hp.Value.ValueKind != JsonValueKind.String)
                {
                    f.Add(new SnapFinding(FindingSeverity.Warning, p, "expected a hotkey string such as \"Win+Alt+Left\"; keeping the default."));
                    continue;
                }

                string error = HotkeySyntax.Check(hp.Value.GetString());
                if (error != null)
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, p, error + " Keeping the default."));
                    continue;
                }

                c.Hotkeys[action] = hp.Value.GetString().Trim();
            }

            // Two actions on one hotkey: the first (in ActionNames order) keeps it.
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string action in SnapConfig.ActionNames)
            {
                if (!c.Hotkeys.TryGetValue(action, out string keys) || string.IsNullOrEmpty(keys)) continue;
                string canonical = HotkeySyntax.Canonical(keys);
                if (used.TryGetValue(canonical, out string other))
                {
                    f.Add(new SnapFinding(FindingSeverity.Error, path + "." + action, $"{keys} is already used by {other}; this action is disabled."));
                    c.Hotkeys[action] = "";
                }
                else
                {
                    used[canonical] = action;
                }
            }
        }

        private static void ReadMonitors(JsonElement v, string path, SnapConfig c, List<SnapFinding> f)
        {
            if (v.ValueKind != JsonValueKind.Object)
            {
                f.Add(new SnapFinding(FindingSeverity.Warning, path, "expected an object mapping monitors to layout ids."));
                return;
            }

            foreach (JsonProperty mp in v.EnumerateObject())
            {
                if (mp.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(mp.Value.GetString()))
                {
                    f.Add(new SnapFinding(FindingSeverity.Warning, path + "." + mp.Name, "expected a layout id."));
                    continue;
                }

                c.MonitorLayouts[mp.Name.Trim()] = mp.Value.GetString().Trim();
            }
        }
    }

    /// <summary>
    /// Syntax-only check of hotkey strings ("Win+Alt+Left"), mirroring what the real parser in CairoDesktop.Customization accepts
    /// closely enough to give early feedback without a WPF reference. The real parser has the final say.
    /// </summary>
    public static class HotkeySyntax
    {
        private static readonly string[] ModifierNames = { "win", "windows", "super", "meta", "ctrl", "control", "alt", "shift" };

        /// <summary>Returns an error message or null when the string looks like modifiers plus exactly one key.</summary>
        public static string Check(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "no keys given.";

            string[] parts = text.Trim().Split('+');
            int modifiers = 0;
            string key = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    // "Ctrl++" means the plus key.
                    if (i == parts.Length - 1 && i > 0 && parts[i - 1].Trim().Length == 0) continue;
                    if (i == parts.Length - 2 && parts[parts.Length - 1].Trim().Length == 0) { part = "plus"; }
                    else return $"'{text}' has an empty key between '+' signs.";
                }

                if (Array.IndexOf(ModifierNames, part.ToLowerInvariant()) >= 0)
                {
                    modifiers++;
                    continue;
                }

                if (key != null) return $"'{text}' has more than one non-modifier key ('{key}' and '{part}').";
                key = part;
            }

            if (key == null) return $"'{text}' has modifiers but no key.";
            if (modifiers == 0 && !IsFunctionKey(key)) return $"'{text}' needs a modifier (Win, Ctrl, Alt or Shift).";
            return null;
        }

        /// <summary>Order- and case-insensitive form for duplicate detection.</summary>
        public static string Canonical(string text)
        {
            var mods = new SortedSet<string>(StringComparer.Ordinal);
            string key = "";
            foreach (string raw in (text ?? "").Split('+'))
            {
                string part = raw.Trim().ToLowerInvariant();
                if (part.Length == 0) { key = "plus"; continue; }
                switch (part)
                {
                    case "windows": case "super": case "meta": part = "win"; break;
                    case "control": part = "ctrl"; break;
                }

                if (part == "win" || part == "ctrl" || part == "alt" || part == "shift") mods.Add(part);
                else key = part;
            }

            return string.Join("+", mods) + "|" + key;
        }

        private static bool IsFunctionKey(string key)
        {
            if (key.Length < 2 || (key[0] != 'f' && key[0] != 'F')) return false;
            return int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 24;
        }
    }
}
