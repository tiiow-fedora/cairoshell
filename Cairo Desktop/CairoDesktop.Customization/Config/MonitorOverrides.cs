using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Customization.Config
{
    /// <summary>Bar settings for the monitors a "monitors" entry in cairo-plus.json selects.</summary>
    public sealed class MonitorConfig
    {
        public BarConfig MenuBar { get; set; }

        public BarConfig Taskbar { get; set; }
    }

    /// <summary>The facts about one monitor that selectors match against.</summary>
    public sealed class MonitorInfo
    {
        /// <summary>Position in the list of monitors, starting at 1, primary first.</summary>
        public int Index { get; set; }

        /// <summary>Windows device name such as \\.\DISPLAY2.</summary>
        public string DeviceName { get; set; }

        public bool IsPrimary { get; set; }

        /// <summary>
        /// Describes a monitor from the device names Windows reports. The primary monitor is number 1; the others
        /// follow in device name order (DISPLAY2 before DISPLAY3).
        /// </summary>
        public static MonitorInfo Create(string deviceName, bool isPrimary, string primaryDeviceName, IEnumerable<string> allDeviceNames)
        {
            int index = 1;
            if (!isPrimary)
            {
                index = 2;
                foreach (string other in allDeviceNames ?? new string[0])
                {
                    if (string.Equals(other, primaryDeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Compare(other, deviceName, StringComparison.OrdinalIgnoreCase) < 0) index++;
                }
            }

            return new MonitorInfo { Index = index, DeviceName = deviceName ?? "", IsPrimary = isPrimary };
        }
    }

    /// <summary>
    /// Finds which "monitors" entries apply to a monitor. A key is one of: <c>*</c> (every monitor), <c>primary</c>,
    /// <c>secondary</c> (every monitor except the primary), a monitor number (<c>2</c>), or a Windows device name
    /// (<c>DISPLAY2</c>, or <c>\\.\DISPLAY2</c>). When several keys match, the more specific one wins field by field:
    /// device name, then number, then primary or secondary, then <c>*</c>.
    /// </summary>
    public static class MonitorOverrides
    {
        public const string SelectorHelp = "use *, primary, secondary, a monitor number such as 2, or a device name such as DISPLAY2";

        /// <summary>Rank of how specific a selector is for this monitor: 0 = does not match, higher = more specific.</summary>
        public static int Match(string key, MonitorInfo monitor)
        {
            if (string.IsNullOrWhiteSpace(key) || monitor == null) return 0;
            string k = key.Trim();

            if (k == "*") return 1;
            if (string.Equals(k, "primary", StringComparison.OrdinalIgnoreCase)) return monitor.IsPrimary ? 2 : 0;
            if (string.Equals(k, "secondary", StringComparison.OrdinalIgnoreCase)) return monitor.IsPrimary ? 0 : 2;

            if (int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                return index == monitor.Index ? 3 : 0;
            }

            string device = NormalizeDevice(k);
            return device.Length > 0 && string.Equals(device, NormalizeDevice(monitor.DeviceName), StringComparison.OrdinalIgnoreCase) ? 4 : 0;
        }

        /// <summary>Null when the key is a valid selector, else a message for the user.</summary>
        public static string ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "has an empty monitor selector (" + SelectorHelp + ").";
            string k = key.Trim();

            if (k == "*" || string.Equals(k, "primary", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "secondary", StringComparison.OrdinalIgnoreCase)) return null;

            if (int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                return index >= 1 ? null : $"'{key}' is not a monitor number (they start at 1).";
            }

            return NormalizeDevice(k).StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) ? null : $"'{key}' is not a monitor selector ({SelectorHelp}).";
        }

        /// <summary>The bar settings for one monitor: <paramref name="baseBar"/> overlaid by every matching entry, most specific last.</summary>
        public static BarConfig Apply(BarConfig baseBar, IDictionary<string, MonitorConfig> monitors, MonitorInfo monitor, bool taskbar)
        {
            if (monitors == null || monitors.Count == 0) return baseBar;

            var matches = new List<KeyValuePair<int, BarConfig>>();
            foreach (var entry in monitors)
            {
                int rank = Match(entry.Key, monitor);
                var bar = taskbar ? entry.Value?.Taskbar : entry.Value?.MenuBar;
                if (rank > 0 && bar != null) matches.Add(new KeyValuePair<int, BarConfig>(rank, bar));
            }

            // Stable sort by rank so equal ranks (for example "DISPLAY2" and "\\.\DISPLAY2") keep file order.
            for (int i = 1; i < matches.Count; i++)
            {
                var item = matches[i];
                int j = i - 1;
                while (j >= 0 && matches[j].Key > item.Key)
                {
                    matches[j + 1] = matches[j];
                    j--;
                }

                matches[j + 1] = item;
            }

            BarConfig result = baseBar;
            foreach (var match in matches) result = BarConfig.Merge(result, match.Value);
            return result;
        }

        private static string NormalizeDevice(string name)
        {
            string n = (name ?? "").Trim();
            while (n.StartsWith("\\", StringComparison.Ordinal) || n.StartsWith(".", StringComparison.Ordinal)) n = n.Substring(1);
            return n;
        }
    }
}
