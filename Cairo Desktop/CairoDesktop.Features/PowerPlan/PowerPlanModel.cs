using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CairoDesktop.Features.PowerPlan
{
    public sealed class PowerPlanInfo
    {
        public PowerPlanInfo(string guid, string name, bool isActive)
        {
            Guid = guid;
            Name = name;
            IsActive = isActive;
        }

        public string Guid { get; }
        public string Name { get; }
        public bool IsActive { get; }
    }

    /// <summary>Reads the output of <c>powercfg /list</c>. Matches on the GUID and the parenthesised name, so it works in any display language.</summary>
    public static class PowerPlanParser
    {
        private static readonly Regex Line = new Regex(
            @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((.*)\)\s*(\*)?\s*$",
            RegexOptions.Compiled);

        public static IList<PowerPlanInfo> Parse(string output)
        {
            var plans = new List<PowerPlanInfo>();
            if (string.IsNullOrEmpty(output)) return plans;
            foreach (string raw in output.Split('\n'))
            {
                Match m = Line.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                string guid = m.Groups[1].Value.ToLowerInvariant();
                if (plans.Any(p => p.Guid == guid)) continue;
                plans.Add(new PowerPlanInfo(guid, m.Groups[2].Value.Trim(), m.Groups[3].Success));
            }

            return plans;
        }
    }

    public sealed class PowerPlanOptions
    {
        public string Label { get; set; } = "Power";
        /// <summary>Plan names (or GUIDs) a click cycles through, in order. Empty = every plan Windows lists.</summary>
        public IList<string> Cycle { get; set; } = new List<string>();
    }

    /// <summary>Picks the next plan to activate and builds the widget text. Pure; the adapter runs powercfg.</summary>
    public sealed class PowerPlanModel
    {
        private readonly PowerPlanOptions _options;

        public PowerPlanModel(PowerPlanOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>The plans a click may select: the configured cycle (matched by name, case-insensitive, or GUID), else all.</summary>
        public IList<PowerPlanInfo> Candidates(IList<PowerPlanInfo> plans)
        {
            if (plans == null || plans.Count == 0) return new List<PowerPlanInfo>();
            if (_options.Cycle == null || _options.Cycle.Count == 0) return plans;
            var chosen = new List<PowerPlanInfo>();
            foreach (string key in _options.Cycle)
            {
                PowerPlanInfo p = plans.FirstOrDefault(x =>
                    string.Equals(x.Name, key?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Guid, key?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (p != null && !chosen.Contains(p)) chosen.Add(p);
            }

            return chosen.Count > 0 ? chosen : plans;
        }

        /// <summary>The plan after the active one in the candidates; the first when the active plan is not among them. Null when there are none.</summary>
        public PowerPlanInfo Next(IList<PowerPlanInfo> plans)
        {
            IList<PowerPlanInfo> list = Candidates(plans);
            if (list.Count == 0) return null;
            int active = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].IsActive) { active = i; break; }
            return list[(active + 1) % list.Count];
        }

        public string FormatText(IList<PowerPlanInfo> plans)
        {
            PowerPlanInfo active = plans?.FirstOrDefault(p => p.IsActive);
            return active == null ? _options.Label + ": ?" : _options.Label + ": " + active.Name;
        }

        public string FormatToolTip(IList<PowerPlanInfo> plans)
        {
            if (plans == null || plans.Count == 0) return "No power plans found. Is powercfg available?";
            return string.Join("\n", plans.Select(p => (p.IsActive ? "* " : "  ") + p.Name)) + "\nClick to switch.";
        }
    }
}
