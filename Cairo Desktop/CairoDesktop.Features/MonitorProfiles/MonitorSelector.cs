using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>
    /// Chooses monitors. Every condition that is set must hold (AND); a selector with nothing set matches every monitor.
    /// </summary>
    public sealed class MonitorSelector
    {
        private const double Epsilon = 1e-9;

        /// <summary>true = primary only, false = non-primary only, null = either.</summary>
        public bool? Primary { get; set; }

        /// <summary>1-based monitor number (primary is 1).</summary>
        public int? Index { get; set; }

        /// <summary>Device name glob (<c>*</c>, <c>?</c>), case-insensitive, matched with and without the <c>\\.\</c> prefix.</summary>
        public string DeviceName { get; set; }

        public int? MinWidth { get; set; }
        public int? MaxWidth { get; set; }
        public int? MinHeight { get; set; }
        public int? MaxHeight { get; set; }

        /// <summary>Landscape = wider than tall, portrait = taller than wide; a square monitor is neither.</summary>
        public MonitorOrientation? Orientation { get; set; }

        /// <summary>Width / height must be at least this (ultrawide 21:9 is about 2.33).</summary>
        public double? MinAspectRatio { get; set; }

        /// <summary>DPI scale must be at least this (1.5 = 150%).</summary>
        public double? MinDpiScale { get; set; }

        public bool IsEmpty => Primary == null && Index == null && DeviceName == null && MinWidth == null && MaxWidth == null
            && MinHeight == null && MaxHeight == null && Orientation == null && MinAspectRatio == null && MinDpiScale == null;

        public bool Matches(MonitorInfo m)
        {
            if (m == null) return false;
            if (Primary != null && Primary.Value != m.IsPrimary) return false;
            if (Index != null && Index.Value != m.Index) return false;
            if (DeviceName != null && !DeviceMatches(m.DeviceName)) return false;
            int w = m.Bounds.Width, h = m.Bounds.Height;
            if (MinWidth != null && w < MinWidth.Value) return false;
            if (MaxWidth != null && w > MaxWidth.Value) return false;
            if (MinHeight != null && h < MinHeight.Value) return false;
            if (MaxHeight != null && h > MaxHeight.Value) return false;
            if (Orientation == MonitorOrientation.Landscape && !(w > h)) return false;
            if (Orientation == MonitorOrientation.Portrait && !(h > w)) return false;
            if (MinAspectRatio != null && m.AspectRatio + Epsilon < MinAspectRatio.Value) return false;
            if (MinDpiScale != null && m.DpiScale + Epsilon < MinDpiScale.Value) return false;
            return true;
        }

        private bool DeviceMatches(string name)
        {
            if (name == null) return false;
            if (GlobMatcher.IsMatch(DeviceName, name)) return true;
            return name.StartsWith(@"\\.\", StringComparison.Ordinal) && GlobMatcher.IsMatch(DeviceName, name.Substring(4));
        }

        /// <summary>
        /// How specific the selector is; higher wins when several rules match. Exact device name 100, monitor number 90,
        /// device glob 40 + literal characters (max 59), primary/secondary 30, min aspect ratio 20, orientation 15,
        /// each width/height bound 12, min dpi 10. A glob of only wildcards counts for nothing.
        /// </summary>
        public int Specificity
        {
            get
            {
                int s = 0;
                if (DeviceName != null)
                {
                    int literals = GlobMatcher.LiteralCount(DeviceName);
                    if (!GlobMatcher.HasWildcard(DeviceName)) s += 100;
                    else if (literals > 0) s += 40 + Math.Min(literals, 19);
                }
                if (Index != null) s += 90;
                if (Primary != null) s += 30;
                if (MinAspectRatio != null) s += 20;
                if (Orientation != null) s += 15;
                if (MinWidth != null) s += 12;
                if (MaxWidth != null) s += 12;
                if (MinHeight != null) s += 12;
                if (MaxHeight != null) s += 12;
                if (MinDpiScale != null) s += 10;
                return s;
            }
        }

        /// <summary>Reasons this selector can never match any monitor (empty when it can).</summary>
        public IReadOnlyList<string> NeverMatchReasons()
        {
            var r = new List<string>();
            if (Index != null && Index.Value < 1) r.Add("index must be 1 or more");
            if (Primary == true && Index != null && Index.Value > 1) r.Add("the primary monitor is always number 1");
            if (Primary == false && Index == 1) r.Add("monitor number 1 is always the primary monitor");
            if (DeviceName != null && DeviceName.Trim().Length == 0) r.Add("deviceName is empty");
            if (MinWidth != null && MaxWidth != null && MinWidth.Value > MaxWidth.Value) r.Add("minWidth is larger than maxWidth");
            if (MinHeight != null && MaxHeight != null && MinHeight.Value > MaxHeight.Value) r.Add("minHeight is larger than maxHeight");
            if (MaxWidth != null && MaxWidth.Value < 1) r.Add("maxWidth is below 1");
            if (MaxHeight != null && MaxHeight.Value < 1) r.Add("maxHeight is below 1");
            if (Orientation == MonitorOrientation.Portrait && MinAspectRatio != null && MinAspectRatio.Value >= 1) r.Add("portrait monitors have an aspect ratio below 1");
            if (Orientation == MonitorOrientation.Landscape && MaxWidth != null && MinHeight != null && MaxWidth.Value <= MinHeight.Value) r.Add("landscape needs width > height, but maxWidth <= minHeight");
            if (Orientation == MonitorOrientation.Portrait && MaxHeight != null && MinWidth != null && MaxHeight.Value <= MinWidth.Value) r.Add("portrait needs height > width, but maxHeight <= minWidth");
            return r;
        }

        /// <summary>
        /// Conservative test: true only when everything this selector matches is certainly matched by
        /// <paramref name="other"/> being narrower, i.e. <c>this</c> is a superset of <paramref name="other"/>.
        /// </summary>
        public bool Covers(MonitorSelector other)
        {
            if (other == null) return false;
            if (Primary != null && Primary != other.Primary) return false;
            if (Index != null && Index != other.Index) return false;
            if (DeviceName != null && !DeviceCovers(DeviceName, other.DeviceName)) return false;
            if (MinWidth != null && !(other.MinWidth != null && other.MinWidth >= MinWidth)) return false;
            if (MaxWidth != null && !(other.MaxWidth != null && other.MaxWidth <= MaxWidth)) return false;
            if (MinHeight != null && !(other.MinHeight != null && other.MinHeight >= MinHeight)) return false;
            if (MaxHeight != null && !(other.MaxHeight != null && other.MaxHeight <= MaxHeight)) return false;
            if (Orientation != null && Orientation != other.Orientation) return false;
            if (MinAspectRatio != null && !(other.MinAspectRatio != null && other.MinAspectRatio >= MinAspectRatio)) return false;
            if (MinDpiScale != null && !(other.MinDpiScale != null && other.MinDpiScale >= MinDpiScale)) return false;
            return true;
        }

        private static bool DeviceCovers(string mine, string theirs)
        {
            if (mine.Replace("*", "").Length == 0) return true;
            if (theirs == null) return false;
            if (string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase)) return true;
            return !GlobMatcher.HasWildcard(theirs) && GlobMatcher.IsMatch(mine, theirs);
        }

        /// <summary>Short text such as <c>primary, width &gt;= 3000</c>; "every monitor" when empty.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Primary != null) parts.Add(Primary.Value ? "primary" : "secondary");
            if (Index != null) parts.Add("index " + Index.Value.ToString(CultureInfo.InvariantCulture));
            if (DeviceName != null) parts.Add("deviceName " + DeviceName);
            if (MinWidth != null) parts.Add("width >= " + MinWidth.Value.ToString(CultureInfo.InvariantCulture));
            if (MaxWidth != null) parts.Add("width <= " + MaxWidth.Value.ToString(CultureInfo.InvariantCulture));
            if (MinHeight != null) parts.Add("height >= " + MinHeight.Value.ToString(CultureInfo.InvariantCulture));
            if (MaxHeight != null) parts.Add("height <= " + MaxHeight.Value.ToString(CultureInfo.InvariantCulture));
            if (Orientation != null) parts.Add(Orientation.Value.ToString().ToLowerInvariant());
            if (MinAspectRatio != null) parts.Add("aspect >= " + MinAspectRatio.Value.ToString("0.##", CultureInfo.InvariantCulture));
            if (MinDpiScale != null) parts.Add("dpi scale >= " + MinDpiScale.Value.ToString("0.##", CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "every monitor" : string.Join(", ", parts);
        }
    }
}
