using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>Integer rectangle in virtual-screen pixels. Deliberately not System.Drawing.Rectangle (keeps the library platform free).</summary>
    public readonly struct MonitorRect : IEquatable<MonitorRect>
    {
        public MonitorRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => Left + Width;
        public int Bottom => Top + Height;

        public bool Equals(MonitorRect other) => Left == other.Left && Top == other.Top && Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is MonitorRect r && Equals(r);
        public override int GetHashCode() => ((Left * 397 ^ Top) * 397 ^ Width) * 397 ^ Height;
        public static bool operator ==(MonitorRect a, MonitorRect b) => a.Equals(b);
        public static bool operator !=(MonitorRect a, MonitorRect b) => !a.Equals(b);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}x{1}@{2},{3}", Width, Height, Left, Top);
    }

    /// <summary>The facts about one monitor that selectors match against. Pure data; the adapter fills it from the OS.</summary>
    public sealed class MonitorInfo
    {
        /// <summary>Windows device name such as \\.\DISPLAY2. This is the monitor's identity across snapshots.</summary>
        public string DeviceName { get; set; } = "";

        /// <summary>Position in the monitor list, starting at 1, primary first (see <see cref="Number"/>).</summary>
        public int Index { get; set; }

        public bool IsPrimary { get; set; }
        public MonitorRect Bounds { get; set; }
        public MonitorRect WorkArea { get; set; }

        /// <summary>1.0 = 96 dpi (100%), 1.5 = 144 dpi (150%).</summary>
        public double DpiScale { get; set; } = 1.0;

        /// <summary>Optional refresh rate in Hz; 0 when unknown.</summary>
        public int RefreshRate { get; set; }

        /// <summary>
        /// Optional hardware identity (for example an EDID based id). When both the old and the new snapshot carry one
        /// and they differ, a reused device name is treated as a different physical monitor.
        /// </summary>
        public string HardwareId { get; set; } = "";

        public string Id => DeviceName ?? "";

        /// <summary>Width divided by height; 0 when the height is not positive.</summary>
        public double AspectRatio => Bounds.Height <= 0 ? 0 : (double)Bounds.Width / Bounds.Height;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} #{1}{2} {3} @{4:0.##}x", DeviceName, Index, IsPrimary ? " primary" : "", Bounds, DpiScale);

        /// <summary>
        /// Numbers monitors the way the existing per-monitor overrides do: primary is 1, the others follow in ordinal,
        /// case-insensitive device name order, exactly as MonitorOverrides.MonitorInfo.Create counts them (so DISPLAY10
        /// sorts before DISPLAY2). Returns a new list ordered by that number and sets <see cref="Index"/>.
        /// </summary>
        public static List<MonitorInfo> Number(IEnumerable<MonitorInfo> monitors)
        {
            var ordered = (monitors ?? Enumerable.Empty<MonitorInfo>())
                .Where(m => m != null)
                .OrderBy(m => m.IsPrimary ? 0 : 1)
                .ThenBy(m => m.DeviceName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Index = i + 1;
            return ordered;
        }
    }
}
