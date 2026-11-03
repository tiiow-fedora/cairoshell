using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CairoDesktop.Features.MonitorProfiles
{
    public enum FindingSeverity { Info, Warning, Error }

    public sealed class ConfigFinding
    {
        public ConfigFinding(FindingSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }

        public FindingSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString() => Severity.ToString().ToLowerInvariant() + " " + Path + ": " + Message;
    }

    public sealed class ProfilesParseResult
    {
        public ProfilesConfig Config { get; } = new ProfilesConfig();
        public List<ConfigFinding> Findings { get; } = new List<ConfigFinding>();
        public bool HasErrors => Findings.Any(f => f.Severity == FindingSeverity.Error);
    }

    /// <summary>
    /// Reads the "monitorProfiles" section from lenient JSON (comments and trailing commas allowed). Never throws: every
    /// problem becomes a finding, and a value that cannot be used is dropped (the property stays unset).
    /// </summary>
    public static class ProfilesConfigParser
    {
        public const string SectionName = "monitorProfiles";

        private static readonly string[] SectionKeys = { "default", "rules" };
        private static readonly string[] RuleKeys = { "name", "match", "profile" };
        private static readonly string[] MatchKeys =
        {
            "primary", "index", "deviceName", "minWidth", "maxWidth", "minHeight", "maxHeight",
            "orientation", "minAspectRatio", "minDpiScale",
        };

        /// <summary>
        /// Accepts either a whole cairo-plus.json (the <c>monitorProfiles</c> property is used) or the section object itself.
        /// Empty input is a valid empty config.
        /// </summary>
        public static ProfilesParseResult Parse(string json)
        {
            var result = new ProfilesParseResult();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using (JsonDocument doc = JsonDocument.Parse(json, options))
                {
                    JsonElement root = doc.RootElement;
                    string path = SectionName;
                    JsonElement section = root;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        Add(result, FindingSeverity.Error, "$", "the JSON root must be an object");
                        return result;
                    }
                    if (TryGet(root, SectionName, out JsonElement inner))
                    {
                        if (inner.ValueKind == JsonValueKind.Null) return result;
                        section = inner;
                    }
                    else if (TryGet(root, "default", out _) || TryGet(root, "rules", out _))
                    {
                        path = "$";
                    }
                    else
                    {
                        return result; // a whole config file without the section: nothing to read, and not ours to flag
                    }
                    ParseSection(section, path, result);
                }
            }
            catch (JsonException ex)
            {
                Add(result, FindingSeverity.Error, "$", "invalid JSON: " + ex.Message);
            }
            catch (Exception ex)
            {
                Add(result, FindingSeverity.Error, "$", "could not read the config: " + ex.Message);
            }
            return result;
        }

        private static void ParseSection(JsonElement section, string path, ProfilesParseResult result)
        {
            if (section.ValueKind != JsonValueKind.Object)
            {
                Add(result, FindingSeverity.Error, path, "must be an object with 'default' and 'rules'");
                return;
            }
            Unknown(section, SectionKeys, path, result);

            if (TryGet(section, "default", out JsonElement def) && def.ValueKind != JsonValueKind.Null)
            {
                result.Config.Default = ParseSettings(def, path + ".default", result);
            }

            if (TryGet(section, "rules", out JsonElement rules) && rules.ValueKind != JsonValueKind.Null)
            {
                if (rules.ValueKind != JsonValueKind.Array)
                {
                    Add(result, FindingSeverity.Error, path + ".rules", "must be an array");
                }
                else
                {
                    int i = 0;
                    foreach (JsonElement r in rules.EnumerateArray())
                    {
                        ProfileRule rule = ParseRule(r, path + ".rules[" + i.ToString(CultureInfo.InvariantCulture) + "]", result);
                        if (rule != null) result.Config.Rules.Add(rule);
                        i++;
                    }
                }
            }

            Analyze(result);
        }

        private static ProfileRule ParseRule(JsonElement el, string path, ProfilesParseResult result)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                Add(result, FindingSeverity.Error, path, "a rule must be an object with 'match' and 'profile'; skipped");
                return null;
            }
            Unknown(el, RuleKeys, path, result);
            var rule = new ProfileRule();
            if (TryGet(el, "name", out JsonElement n) && n.ValueKind != JsonValueKind.Null)
            {
                if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) rule.Name = n.GetString().Trim();
                else Add(result, FindingSeverity.Warning, path + ".name", "should be a non-empty string");
            }
            if (TryGet(el, "match", out JsonElement m) && m.ValueKind != JsonValueKind.Null)
            {
                // A condition that cannot be read would silently widen the rule (down to every monitor), so skip the rule instead.
                int errorsBefore = result.Findings.Count(f => f.Severity == FindingSeverity.Error);
                rule.Match = ParseMatch(m, path + ".match", result);
                if (result.Findings.Count(f => f.Severity == FindingSeverity.Error) > errorsBefore)
                {
                    Add(result, FindingSeverity.Error, path, "its 'match' has invalid values, so the rule is skipped rather than applied to more monitors than intended");
                    return null;
                }
            }
            if (TryGet(el, "profile", out JsonElement p) && p.ValueKind != JsonValueKind.Null) rule.Settings = ParseSettings(p, path + ".profile", result);
            return rule;
        }

        private static MonitorSelector ParseMatch(JsonElement el, string path, ProfilesParseResult result)
        {
            var sel = new MonitorSelector();
            if (el.ValueKind != JsonValueKind.Object)
            {
                Add(result, FindingSeverity.Error, path, "must be an object");
                return sel;
            }
            Unknown(el, MatchKeys, path, result);
            if (TryGet(el, "primary", out JsonElement v) && v.ValueKind != JsonValueKind.Null) sel.Primary = Bool(v, path + ".primary", result);
            if (TryGet(el, "index", out v) && v.ValueKind != JsonValueKind.Null) sel.Index = Int(v, path + ".index", result);
            if (TryGet(el, "deviceName", out v) && v.ValueKind != JsonValueKind.Null) sel.DeviceName = Str(v, path + ".deviceName", result)?.Trim();
            if (TryGet(el, "minWidth", out v) && v.ValueKind != JsonValueKind.Null) sel.MinWidth = Positive(v, path + ".minWidth", result);
            if (TryGet(el, "maxWidth", out v) && v.ValueKind != JsonValueKind.Null) sel.MaxWidth = Positive(v, path + ".maxWidth", result);
            if (TryGet(el, "minHeight", out v) && v.ValueKind != JsonValueKind.Null) sel.MinHeight = Positive(v, path + ".minHeight", result);
            if (TryGet(el, "maxHeight", out v) && v.ValueKind != JsonValueKind.Null) sel.MaxHeight = Positive(v, path + ".maxHeight", result);
            if (TryGet(el, "orientation", out v) && v.ValueKind != JsonValueKind.Null) sel.Orientation = ParseEnum<MonitorOrientation>(v, path + ".orientation", result);
            if (TryGet(el, "minAspectRatio", out v) && v.ValueKind != JsonValueKind.Null) sel.MinAspectRatio = PositiveDouble(v, path + ".minAspectRatio", result);
            if (TryGet(el, "minDpiScale", out v) && v.ValueKind != JsonValueKind.Null) sel.MinDpiScale = PositiveDouble(v, path + ".minDpiScale", result);
            return sel;
        }

        private static ProfileSettings ParseSettings(JsonElement el, string path, ProfilesParseResult result)
        {
            var s = new ProfileSettings();
            if (el.ValueKind != JsonValueKind.Object)
            {
                Add(result, FindingSeverity.Error, path, "must be an object");
                return s;
            }
            Unknown(el, ProfileSettings.PropertyNames, path, result);
            JsonElement v;
            if (TryGet(el, "position", out v) && v.ValueKind != JsonValueKind.Null) s.Position = ParseEnum<TaskbarEdge>(v, path + ".position", result);
            if (TryGet(el, "height", out v) && v.ValueKind != JsonValueKind.Null) s.Height = Positive(v, path + ".height", result);
            if (TryGet(el, "autoHide", out v) && v.ValueKind != JsonValueKind.Null) s.AutoHide = Bool(v, path + ".autoHide", result);
            if (TryGet(el, "floating", out v) && v.ValueKind != JsonValueKind.Null) s.Floating = Bool(v, path + ".floating", result);
            if (TryGet(el, "margin", out v) && v.ValueKind != JsonValueKind.Null) s.Margin = NonNegative(v, path + ".margin", result);
            if (TryGet(el, "layoutName", out v) && v.ValueKind != JsonValueKind.Null)
            {
                string name = Str(v, path + ".layoutName", result);
                if (name != null && name.Trim().Length == 0) Add(result, FindingSeverity.Warning, path + ".layoutName", "is empty; ignored");
                else s.LayoutName = name?.Trim();
            }
            if (TryGet(el, "themeVariant", out v) && v.ValueKind != JsonValueKind.Null) s.ThemeVariant = ParseEnum<ThemeVariant>(v, path + ".themeVariant", result);
            if (TryGet(el, "showClock", out v) && v.ValueKind != JsonValueKind.Null) s.ShowClock = Bool(v, path + ".showClock", result);
            if (TryGet(el, "showTaskList", out v) && v.ValueKind != JsonValueKind.Null) s.ShowTaskList = Bool(v, path + ".showTaskList", result);
            if (TryGet(el, "iconSize", out v) && v.ValueKind != JsonValueKind.Null) s.IconSize = Positive(v, path + ".iconSize", result);
            if (TryGet(el, "hideOnFullscreen", out v) && v.ValueKind != JsonValueKind.Null) s.HideOnFullscreen = Bool(v, path + ".hideOnFullscreen", result);
            return s;
        }

        // ---- cross-rule analysis -------------------------------------------------------------------------------

        private static void Analyze(ProfilesParseResult result)
        {
            List<ProfileRule> rules = result.Config.Rules;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rules.Count; i++)
            {
                ProfileRule r = rules[i];
                string label = "rule '" + r.DisplayName(i) + "'";
                string path = "rules[" + i.ToString(CultureInfo.InvariantCulture) + "]";

                if (!string.IsNullOrWhiteSpace(r.Name))
                {
                    if (seen.TryGetValue(r.Name, out int first))
                        Add(result, FindingSeverity.Warning, path + ".name", "duplicate rule name '" + r.Name + "' (also rule #" + (first + 1).ToString(CultureInfo.InvariantCulture) + "); explanations will be ambiguous");
                    else seen[r.Name] = i;
                }

                foreach (string reason in r.Match.NeverMatchReasons())
                    Add(result, FindingSeverity.Warning, path + ".match", label + " can never match: " + reason);

                if (!r.Settings.SetPropertyNames().Any())
                    Add(result, FindingSeverity.Warning, path + ".profile", label + " sets nothing");
                if (r.Match.IsEmpty)
                    Add(result, FindingSeverity.Info, path + ".match", label + " matches every monitor; consider using 'default' instead");
            }

            // Application order: specificity ascending, then file order. A rule is unreachable when a rule applied after
            // it covers at least the same monitors and sets every property it sets.
            var order = Enumerable.Range(0, rules.Count).OrderBy(i => rules[i].Match.Specificity).ThenBy(i => i).ToList();
            for (int a = 0; a < order.Count; a++)
            {
                ProfileRule early = rules[order[a]];
                var props = early.Settings.SetPropertyNames().ToList();
                if (props.Count == 0 || early.Match.NeverMatchReasons().Count > 0) continue;
                for (int b = a + 1; b < order.Count; b++)
                {
                    ProfileRule late = rules[order[b]];
                    if (!late.Match.Covers(early.Match)) continue;
                    if (!props.All(p => late.Settings.IsSet(p))) continue;
                    Add(result, FindingSeverity.Warning, "rules[" + order[a].ToString(CultureInfo.InvariantCulture) + "]",
                        "rule '" + early.DisplayName(order[a]) + "' is unreachable: rule '" + late.DisplayName(order[b]) + "' applies after it, matches the same monitors and sets all of its properties");
                    break;
                }
            }
        }

        // ---- value helpers -------------------------------------------------------------------------------------

        private static bool TryGet(JsonElement obj, string name, out JsonElement value)
        {
            // Last one wins, as when ConfigLoader deserializes the same file.
            bool found = false;
            value = default(JsonElement);
            foreach (JsonProperty p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    found = true;
                }
            }
            return found;
        }

        private static void Unknown(JsonElement obj, IEnumerable<string> known, string path, ProfilesParseResult result)
        {
            var set = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in obj.EnumerateObject())
            {
                if (!set.Contains(p.Name))
                    Add(result, FindingSeverity.Warning, path + "." + p.Name, "unknown key '" + p.Name + "' (expected one of: " + string.Join(", ", known) + ")");
            }
        }

        private static bool? Bool(JsonElement v, string path, ProfilesParseResult result)
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            Add(result, FindingSeverity.Error, path, "must be true or false");
            return null;
        }

        private static string Str(JsonElement v, string path, ProfilesParseResult result)
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            Add(result, FindingSeverity.Error, path, "must be a string");
            return null;
        }

        private static int? Int(JsonElement v, string path, ProfilesParseResult result)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
            Add(result, FindingSeverity.Error, path, "must be a whole number");
            return null;
        }

        private static int? Positive(JsonElement v, string path, ProfilesParseResult result)
        {
            int? i = Int(v, path, result);
            if (i != null && i.Value <= 0)
            {
                Add(result, FindingSeverity.Error, path, "must be greater than 0 (got " + i.Value.ToString(CultureInfo.InvariantCulture) + ")");
                return null;
            }
            return i;
        }

        private static int? NonNegative(JsonElement v, string path, ProfilesParseResult result)
        {
            int? i = Int(v, path, result);
            if (i != null && i.Value < 0)
            {
                Add(result, FindingSeverity.Error, path, "must not be negative (got " + i.Value.ToString(CultureInfo.InvariantCulture) + ")");
                return null;
            }
            return i;
        }

        private static double? PositiveDouble(JsonElement v, string path, ProfilesParseResult result)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
            {
                if (d > 0) return d;
                Add(result, FindingSeverity.Error, path, "must be greater than 0");
                return null;
            }
            Add(result, FindingSeverity.Error, path, "must be a number");
            return null;
        }

        private static T? ParseEnum<T>(JsonElement v, string path, ProfilesParseResult result) where T : struct
        {
            string text = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (text != null)
            {
                foreach (T candidate in System.Enum.GetValues(typeof(T)))
                    if (string.Equals(candidate.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            string allowed = string.Join(", ", System.Enum.GetNames(typeof(T)).Select(x => x.ToLowerInvariant()));
            Add(result, FindingSeverity.Error, path, "must be one of: " + allowed + (text != null ? " (got '" + text + "')" : ""));
            return null;
        }

        private static void Add(ProfilesParseResult r, FindingSeverity s, string path, string message) => r.Findings.Add(new ConfigFinding(s, path, message));
    }
}
