using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>The settings one monitor ends up with, and which layer supplied each property.</summary>
    public sealed class ResolvedProfile
    {
        internal ResolvedProfile(MonitorInfo monitor, ProfileSettings settings, Dictionary<string, string> sources, List<string> matchedRules)
        {
            Monitor = monitor;
            Settings = settings;
            Sources = sources;
            MatchedRules = matchedRules;
        }

        public MonitorInfo Monitor { get; }

        /// <summary>Effective settings. All properties are set except layoutName, which stays null unless a layer sets it.</summary>
        public ProfileSettings Settings { get; }

        /// <summary>Property name to the layer that supplied it: "built-in", "default" or "rule 'name'".</summary>
        public IReadOnlyDictionary<string, string> Sources { get; }

        /// <summary>Names of matching rules in the order they were applied (least specific first).</summary>
        public IReadOnlyList<string> MatchedRules { get; }

        public bool SettingsEqual(ResolvedProfile other) => other != null && Settings.ValueEquals(other.Settings);

        /// <summary>Multi-line text for a <c>--explain-monitors</c> style command.</summary>
        public string Explain()
        {
            var sb = new StringBuilder();
            sb.Append(Monitor.ToString()).AppendLine();
            sb.Append("  rules applied: ").Append(MatchedRules.Count == 0 ? "(none)" : string.Join(" -> ", MatchedRules)).AppendLine();
            foreach (string name in ProfileSettings.PropertyNames)
            {
                sb.Append("  ").Append(name.PadRight(17)).Append(Settings.Format(name).PadRight(10)).Append(" <- ").Append(Sources[name]).AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>Turns monitors plus a config into per-monitor taskbar settings.</summary>
    public static class ProfileResolver
    {
        public const string BuiltInSource = "built-in";
        public const string DefaultSource = "default";

        /// <summary>
        /// Layering: built-in defaults, then <c>default</c>, then every matching rule ordered by selector specificity
        /// (ties keep file order, later wins). Per property the last layer that sets it wins; unset properties fall through.
        /// Key is the monitor's device name; a repeated device name keeps the first entry.
        /// </summary>
        public static Dictionary<string, ResolvedProfile> Resolve(IReadOnlyList<MonitorInfo> monitors, ProfilesConfig config)
        {
            var result = new Dictionary<string, ResolvedProfile>(StringComparer.OrdinalIgnoreCase);
            if (monitors == null) return result;
            config = config ?? new ProfilesConfig();

            var rules = (config.Rules ?? new List<ProfileRule>())
                .Select((r, i) => new { Rule = r, Index = i })
                .Where(x => x.Rule != null)
                .OrderBy(x => (x.Rule.Match ?? new MonitorSelector()).Specificity)
                .ThenBy(x => x.Index)
                .ToList();

            foreach (MonitorInfo m in monitors)
            {
                if (m == null || result.ContainsKey(m.Id)) continue;

                var settings = new ProfileSettings();
                var sources = new Dictionary<string, string>();
                var matched = new List<string>();

                Layer(settings, sources, ProfileSettings.BuiltInDefaults(), BuiltInSource);
                Layer(settings, sources, config.Default, DefaultSource);
                foreach (var x in rules)
                {
                    if (!(x.Rule.Match ?? new MonitorSelector()).Matches(m)) continue;
                    string name = x.Rule.DisplayName(x.Index);
                    matched.Add(name);
                    Layer(settings, sources, x.Rule.Settings, "rule '" + name + "'");
                }
                foreach (string p in ProfileSettings.PropertyNames) if (!sources.ContainsKey(p)) sources[p] = BuiltInSource + " (unset)";

                result[m.Id] = new ResolvedProfile(m, settings, sources, matched);
            }
            return result;
        }

        /// <summary>Explanation of every monitor, in the order given.</summary>
        public static string ExplainAll(IReadOnlyList<MonitorInfo> monitors, ProfilesConfig config)
        {
            var resolved = Resolve(monitors, config);
            var sb = new StringBuilder();
            foreach (MonitorInfo m in monitors ?? new MonitorInfo[0])
            {
                if (m != null && resolved.TryGetValue(m.Id, out ResolvedProfile p)) sb.Append(p.Explain());
            }
            return sb.ToString();
        }

        /// <summary>Device names whose effective settings differ between two resolutions (or exist in only one).</summary>
        public static List<string> ChangedMonitors(IDictionary<string, ResolvedProfile> before, IDictionary<string, ResolvedProfile> after)
        {
            var changed = new List<string>();
            foreach (var kv in after)
            {
                if (before == null || !before.TryGetValue(kv.Key, out ResolvedProfile old) || !old.SettingsEqual(kv.Value)) changed.Add(kv.Key);
            }
            if (before != null) foreach (string k in before.Keys) if (!after.ContainsKey(k)) changed.Add(k);
            return changed;
        }

        private static void Layer(ProfileSettings target, Dictionary<string, string> sources, ProfileSettings layer, string source)
        {
            if (layer == null) return;
            foreach (string name in layer.SetPropertyNames())
            {
                target.Set(name, layer.Get(name));
                sources[name] = source;
            }
        }
    }
}
