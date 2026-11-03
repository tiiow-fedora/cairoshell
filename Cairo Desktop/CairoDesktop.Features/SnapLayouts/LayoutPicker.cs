using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>What the picker needs to know about a monitor.</summary>
    public sealed class MonitorDescriptor
    {
        public MonitorDescriptor(string deviceName, int width, int height, bool isPrimary = false)
        {
            DeviceName = deviceName ?? "";
            Width = width;
            Height = height;
            IsPrimary = isPrimary;
        }

        /// <summary>e.g. \\.\DISPLAY2</summary>
        public string DeviceName { get; }
        public int Width { get; }
        public int Height { get; }
        public bool IsPrimary { get; }

        public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
    }

    /// <summary>Chooses the layout to use on a monitor.</summary>
    public static class LayoutPicker
    {
        /// <summary>Width/height at or above which a monitor counts as ultrawide (21:9 is about 2.33, 16:10 is 1.6).</summary>
        public const double UltrawideAspect = 2.2;

        /// <summary>
        /// Order of precedence: (1) the user's per-monitor map, looked up by device name, then "WIDTHxHEIGHT", then "primary";
        /// (2) the configured default layout; (3) by shape: portrait (taller than wide) gets top/bottom halves,
        /// aspect &gt;= <see cref="UltrawideAspect"/> gets 25/50/25, everything else gets left/right halves.
        /// Ids that don't exist in the catalog are skipped, so the result is never null.
        /// </summary>
        public static SnapLayout Pick(LayoutCatalog catalog, MonitorDescriptor monitor, IDictionary<string, string> monitorMap = null, string defaultLayoutId = null)
        {
            catalog = catalog ?? new LayoutCatalog();

            if (monitor != null && monitorMap != null && monitorMap.Count > 0)
            {
                var keys = new List<string>();
                if (!string.IsNullOrWhiteSpace(monitor.DeviceName)) keys.Add(monitor.DeviceName.Trim());
                keys.Add(monitor.Width.ToString(CultureInfo.InvariantCulture) + "x" + monitor.Height.ToString(CultureInfo.InvariantCulture));
                if (monitor.IsPrimary) keys.Add("primary");

                foreach (string key in keys)
                {
                    string id = Lookup(monitorMap, key);
                    SnapLayout layout = catalog.Find(id);
                    if (layout != null) return layout;
                }
            }

            SnapLayout configured = catalog.Find(defaultLayoutId);
            if (configured != null) return configured;

            return catalog.Find(ByShape(monitor)) ?? catalog.Default;
        }

        /// <summary>The built-in layout id suited to the monitor's shape.</summary>
        public static string ByShape(MonitorDescriptor monitor)
        {
            if (monitor == null || monitor.Width <= 0 || monitor.Height <= 0) return BuiltInLayouts.HalvesLr;
            if (monitor.Width < monitor.Height) return BuiltInLayouts.HalvesTb;
            if (monitor.AspectRatio >= UltrawideAspect) return BuiltInLayouts.Ultrawide;
            return BuiltInLayouts.HalvesLr;
        }

        private static string Lookup(IDictionary<string, string> map, string key)
        {
            foreach (KeyValuePair<string, string> entry in map)
            {
                if (string.Equals(entry.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            }

            return null;
        }
    }
}
