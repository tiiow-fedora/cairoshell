using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>The same monitor in two snapshots.</summary>
    public sealed class MonitorChange
    {
        public MonitorChange(MonitorInfo before, MonitorInfo after)
        {
            Before = before;
            After = after;
        }

        public MonitorInfo Before { get; }
        public MonitorInfo After { get; }
    }

    /// <summary>What changed between two monitor snapshots.</summary>
    public sealed class TopologyDiff
    {
        public List<MonitorInfo> Added { get; } = new List<MonitorInfo>();
        public List<MonitorInfo> Removed { get; } = new List<MonitorInfo>();

        /// <summary>Same monitor, different bounds (position or resolution).</summary>
        public List<MonitorChange> Moved { get; } = new List<MonitorChange>();

        public List<MonitorChange> DpiChanged { get; } = new List<MonitorChange>();

        /// <summary>Same bounds and no matching device name: Windows renumbered the monitor.</summary>
        public List<MonitorChange> Renamed { get; } = new List<MonitorChange>();

        public bool PrimaryChanged { get; internal set; }
        public MonitorInfo OldPrimary { get; internal set; }
        public MonitorInfo NewPrimary { get; internal set; }

        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Moved.Count == 0 && DpiChanged.Count == 0 && Renamed.Count == 0 && !PrimaryChanged;

        /// <summary>
        /// Device names (new snapshot) whose bars must be rebuilt: added, moved, DPI changed, renamed, and both ends of a
        /// primary change. Removed monitors are in <see cref="RemovedIds"/>.
        /// </summary>
        public IReadOnlyList<string> AffectedIds
        {
            get
            {
                var ids = new List<string>();
                void Add(MonitorInfo m)
                {
                    if (m != null && !ids.Contains(m.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(m.Id);
                }
                foreach (var m in Added) Add(m);
                foreach (var c in Moved) Add(c.After);
                foreach (var c in DpiChanged) Add(c.After);
                foreach (var c in Renamed) Add(c.After);
                if (PrimaryChanged)
                {
                    Add(NewPrimary);
                    if (OldPrimary != null) Add(Counterpart(OldPrimary));
                }
                return ids;
            }
        }

        public IReadOnlyList<string> RemovedIds => Removed.Select(m => m.Id).ToList();

        internal Func<MonitorInfo, MonitorInfo> CounterpartLookup { get; set; }

        private MonitorInfo Counterpart(MonitorInfo old) => CounterpartLookup == null ? null : CounterpartLookup(old);

        public override string ToString() => IsEmpty
            ? "no change"
            : string.Format("added {0}, removed {1}, moved {2}, dpi {3}, renamed {4}, primary {5}",
                Added.Count, Removed.Count, Moved.Count, DpiChanged.Count, Renamed.Count, PrimaryChanged ? "changed" : "same");
    }

    /// <summary>
    /// Compares two monitor snapshots with a stable identity. Pairing, in order: the same device name (unless both sides
    /// carry a hardware id and they differ); then leftovers with identical bounds (a renumbered monitor). Anything
    /// still unpaired is added or removed.
    /// </summary>
    public static class MonitorTopologyDiff
    {
        public static TopologyDiff Compare(IReadOnlyList<MonitorInfo> oldMonitors, IReadOnlyList<MonitorInfo> newMonitors)
        {
            var olds = Distinct(oldMonitors);
            var news = Distinct(newMonitors);
            var diff = new TopologyDiff();

            var pairs = new List<KeyValuePair<MonitorInfo, MonitorInfo>>();
            var freeOld = new List<MonitorInfo>(olds);
            var freeNew = new List<MonitorInfo>();

            foreach (MonitorInfo n in news)
            {
                MonitorInfo o = freeOld.FirstOrDefault(x => string.Equals(x.Id, n.Id, StringComparison.OrdinalIgnoreCase) && SameHardware(x, n));
                if (o != null)
                {
                    freeOld.Remove(o);
                    pairs.Add(new KeyValuePair<MonitorInfo, MonitorInfo>(o, n));
                }
                else
                {
                    freeNew.Add(n);
                }
            }

            foreach (MonitorInfo n in freeNew.ToList())
            {
                MonitorInfo o = freeOld.FirstOrDefault(x => x.Bounds == n.Bounds && SameHardware(x, n));
                if (o == null) continue;
                freeOld.Remove(o);
                freeNew.Remove(n);
                pairs.Add(new KeyValuePair<MonitorInfo, MonitorInfo>(o, n));
                diff.Renamed.Add(new MonitorChange(o, n));
            }

            diff.Added.AddRange(freeNew);
            diff.Removed.AddRange(freeOld);

            foreach (var p in pairs.OrderBy(p => news.IndexOf(p.Value)))
            {
                var change = new MonitorChange(p.Key, p.Value);
                if (p.Key.Bounds != p.Value.Bounds) diff.Moved.Add(change);
                if (Math.Abs(p.Key.DpiScale - p.Value.DpiScale) > 0.001) diff.DpiChanged.Add(change);
            }
            diff.Renamed.Sort((a, b) => news.IndexOf(a.After).CompareTo(news.IndexOf(b.After)));

            diff.OldPrimary = olds.FirstOrDefault(m => m.IsPrimary);
            diff.NewPrimary = news.FirstOrDefault(m => m.IsPrimary);
            diff.CounterpartLookup = o => pairs.Where(p => ReferenceEquals(p.Key, o)).Select(p => p.Value).FirstOrDefault();
            if (diff.OldPrimary == null || diff.NewPrimary == null)
            {
                diff.PrimaryChanged = diff.OldPrimary != diff.NewPrimary;
            }
            else
            {
                var counterpart = diff.CounterpartLookup(diff.OldPrimary);
                diff.PrimaryChanged = !ReferenceEquals(counterpart, diff.NewPrimary);
            }

            return diff;
        }

        private static bool SameHardware(MonitorInfo a, MonitorInfo b) =>
            string.IsNullOrEmpty(a.HardwareId) || string.IsNullOrEmpty(b.HardwareId)
            || string.Equals(a.HardwareId, b.HardwareId, StringComparison.OrdinalIgnoreCase);

        private static List<MonitorInfo> Distinct(IReadOnlyList<MonitorInfo> list)
        {
            var result = new List<MonitorInfo>();
            if (list == null) return result;
            foreach (var m in list)
            {
                if (m != null && !result.Any(x => string.Equals(x.Id, m.Id, StringComparison.OrdinalIgnoreCase))) result.Add(m);
            }
            return result;
        }
    }
}
