using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CairoDesktop.Features.WorldClock
{
    public sealed class ClockZoneSpec
    {
        public ClockZoneSpec(string label, string zoneId)
        {
            Label = label;
            ZoneId = zoneId;
        }

        public string Label { get; }
        public string ZoneId { get; }

        /// <summary>
        /// "Tokyo=Asia/Tokyo" gives the label Tokyo; a bare "Asia/Tokyo" is labelled from the last part of its id ("Tokyo",
        /// "New_York" becomes "New York"). Returns null for blank text or a missing id.
        /// </summary>
        public static ClockZoneSpec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string label = null;
            string id = text.Trim();
            int eq = id.IndexOf('=');
            if (eq >= 0)
            {
                label = id.Substring(0, eq).Trim();
                id = id.Substring(eq + 1).Trim();
            }

            if (id.Length == 0) return null;
            if (string.IsNullOrEmpty(label))
            {
                int slash = id.LastIndexOf('/');
                label = (slash >= 0 ? id.Substring(slash + 1) : id).Replace('_', ' ');
            }

            return new ClockZoneSpec(label, id);
        }

        /// <summary>Splits "a=b;c=d" (also commas and line breaks) into specs, skipping blanks.</summary>
        public static IList<ClockZoneSpec> ParseList(string text)
        {
            var result = new List<ClockZoneSpec>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                ClockZoneSpec spec = Parse(part);
                if (spec != null) result.Add(spec);
            }

            return result;
        }
    }

    /// <summary>
    /// Finds a time zone by IANA id ("Europe/London") or Windows id ("GMT Standard Time"), whichever the machine knows.
    /// .NET Framework only knows Windows ids and some Linux boxes only IANA ones, so common zones carry both names.
    /// </summary>
    public static class ZoneResolver
    {
        // IANA id, Windows id.
        private static readonly string[][] Pairs =
        {
            new[] { "UTC", "UTC" },
            new[] { "Europe/London", "GMT Standard Time" },
            new[] { "Europe/Dublin", "GMT Standard Time" },
            new[] { "Europe/Paris", "Romance Standard Time" },
            new[] { "Europe/Berlin", "W. Europe Standard Time" },
            new[] { "Europe/Madrid", "Romance Standard Time" },
            new[] { "Europe/Rome", "W. Europe Standard Time" },
            new[] { "Europe/Amsterdam", "W. Europe Standard Time" },
            new[] { "Europe/Stockholm", "W. Europe Standard Time" },
            new[] { "Europe/Warsaw", "Central European Standard Time" },
            new[] { "Europe/Athens", "GTB Standard Time" },
            new[] { "Europe/Helsinki", "FLE Standard Time" },
            new[] { "Europe/Istanbul", "Turkey Standard Time" },
            new[] { "Europe/Moscow", "Russian Standard Time" },
            new[] { "America/New_York", "Eastern Standard Time" },
            new[] { "America/Chicago", "Central Standard Time" },
            new[] { "America/Denver", "Mountain Standard Time" },
            new[] { "America/Phoenix", "US Mountain Standard Time" },
            new[] { "America/Los_Angeles", "Pacific Standard Time" },
            new[] { "America/Anchorage", "Alaskan Standard Time" },
            new[] { "America/Toronto", "Eastern Standard Time" },
            new[] { "America/Vancouver", "Pacific Standard Time" },
            new[] { "America/Mexico_City", "Central Standard Time (Mexico)" },
            new[] { "America/Sao_Paulo", "E. South America Standard Time" },
            new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" },
            new[] { "Pacific/Honolulu", "Hawaiian Standard Time" },
            new[] { "Asia/Dubai", "Arabian Standard Time" },
            new[] { "Asia/Karachi", "Pakistan Standard Time" },
            new[] { "Asia/Kolkata", "India Standard Time" },
            new[] { "Asia/Dhaka", "Bangladesh Standard Time" },
            new[] { "Asia/Bangkok", "SE Asia Standard Time" },
            new[] { "Asia/Singapore", "Singapore Standard Time" },
            new[] { "Asia/Hong_Kong", "China Standard Time" },
            new[] { "Asia/Shanghai", "China Standard Time" },
            new[] { "Asia/Tokyo", "Tokyo Standard Time" },
            new[] { "Asia/Seoul", "Korea Standard Time" },
            new[] { "Australia/Perth", "W. Australia Standard Time" },
            new[] { "Australia/Sydney", "AUS Eastern Standard Time" },
            new[] { "Australia/Melbourne", "AUS Eastern Standard Time" },
            new[] { "Pacific/Auckland", "New Zealand Standard Time" },
            new[] { "Africa/Cairo", "Egypt Standard Time" },
            new[] { "Africa/Johannesburg", "South Africa Standard Time" },
            new[] { "Africa/Lagos", "W. Central Africa Standard Time" },
            new[] { "Africa/Nairobi", "E. Africa Standard Time" },
        };

        /// <summary>
        /// Tries the id as given, then the other naming scheme. <paramref name="finder"/> wraps
        /// <c>TimeZoneInfo.FindSystemTimeZoneById</c> (it may throw or return null for an unknown id). Returns null when nothing matches.
        /// </summary>
        public static TimeZoneInfo Find(string id, Func<string, TimeZoneInfo> finder)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            id = id.Trim();
            foreach (string candidate in Candidates(id))
            {
                try
                {
                    TimeZoneInfo zone = finder(candidate);
                    if (zone != null) return zone;
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException || ex is ArgumentException || ex is System.Security.SecurityException)
                {
                    // try the next name
                }
            }

            return null;
        }

        private static IEnumerable<string> Candidates(string id)
        {
            yield return id;
            foreach (string[] pair in Pairs)
            {
                if (string.Equals(pair[0], id, StringComparison.OrdinalIgnoreCase)) yield return pair[1];
                else if (string.Equals(pair[1], id, StringComparison.OrdinalIgnoreCase)) yield return pair[0];
            }
        }
    }

    public sealed class WorldClockOptions
    {
        /// <summary>.NET time format for each zone, for example "HH:mm" or "h:mm tt".</summary>
        public string Format { get; set; } = "HH:mm";
        public string Separator { get; set; } = "   ";
        public bool ShowLabels { get; set; } = true;
        /// <summary>Append "+1d" / "-1d" when the zone's date differs from the PC's.</summary>
        public bool ShowDayDelta { get; set; } = true;
        /// <summary>Tooltip marks zones where it is a weekday between these local hours.</summary>
        public int WorkStartHour { get; set; } = 9;
        public int WorkEndHour { get; set; } = 18;
        /// <summary>Tooltip line format (date and time of the zone).</summary>
        public string ToolTipFormat { get; set; } = "ddd d MMM  HH:mm";
    }

    public sealed class WorldClockView
    {
        internal WorldClockView(string text, string toolTip, IList<string> problems)
        {
            Text = text;
            ToolTip = toolTip;
            Problems = problems;
        }

        public string Text { get; }
        public string ToolTip { get; }
        /// <summary>Unknown zone ids and bad formats. Show or log them once; the text still renders for the zones that work.</summary>
        public IList<string> Problems { get; }
    }

    /// <summary>Builds the widget text for a list of zones. Pure: pass the time, the local zone and a finder.</summary>
    public sealed class WorldClockFormatter
    {
        private readonly IList<ClockZoneSpec> _specs;
        private readonly WorldClockOptions _options;
        private readonly Func<string, TimeZoneInfo> _finder;
        private readonly CultureInfo _culture;
        private readonly Dictionary<string, TimeZoneInfo> _cache = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);

        public WorldClockFormatter(IList<ClockZoneSpec> specs, WorldClockOptions options, Func<string, TimeZoneInfo> finder, CultureInfo culture)
        {
            _specs = specs ?? new List<ClockZoneSpec>();
            _options = options ?? new WorldClockOptions();
            _finder = finder ?? TimeZoneInfo.FindSystemTimeZoneById;
            _culture = culture ?? CultureInfo.InvariantCulture;
        }

        public WorldClockView Render(DateTimeOffset now, TimeZoneInfo localZone)
        {
            var problems = new List<string>();
            var parts = new List<string>();
            var tips = new List<string>();
            DateTime localDate = TimeZoneInfo.ConvertTime(now, localZone).Date;

            foreach (ClockZoneSpec spec in _specs)
            {
                TimeZoneInfo zone = Lookup(spec.ZoneId);
                if (zone == null)
                {
                    problems.Add("Unknown time zone '" + spec.ZoneId + "'.");
                    continue;
                }

                DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
                int delta = (int)Math.Round((local.Date - localDate).TotalDays);
                string time = SafeFormat(local, _options.Format, "HH:mm", problems);

                var sb = new StringBuilder();
                if (_options.ShowLabels) sb.Append(spec.Label).Append(' ');
                sb.Append(time);
                if (_options.ShowDayDelta && delta != 0) sb.Append(' ').Append(delta > 0 ? "+" : "").Append(delta).Append('d');
                parts.Add(sb.ToString());

                string tip = spec.Label + ": " + SafeFormat(local, _options.ToolTipFormat, "ddd d MMM  HH:mm", problems);
                if (delta != 0) tip += $" ({(delta > 0 ? "+" : "")}{delta} day{(Math.Abs(delta) == 1 ? "" : "s")})";
                tip += "  UTC" + FormatOffset(zone.GetUtcOffset(now));
                if (IsWorkTime(local)) tip += "  [work hours]";
                tips.Add(tip);
            }

            return new WorldClockView(string.Join(_options.Separator ?? "   ", parts), string.Join("\n", tips), problems.Distinct().ToList());
        }

        public bool IsWorkTime(DateTime local)
            => local.DayOfWeek != DayOfWeek.Saturday && local.DayOfWeek != DayOfWeek.Sunday
               && local.Hour >= _options.WorkStartHour && local.Hour < _options.WorkEndHour;

        public static string FormatOffset(TimeSpan offset)
        {
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            offset = offset.Duration();
            return sign + offset.Hours.ToString("00", CultureInfo.InvariantCulture) + (offset.Minutes != 0 ? ":" + offset.Minutes.ToString("00", CultureInfo.InvariantCulture) : "");
        }

        private TimeZoneInfo Lookup(string id)
        {
            if (_cache.TryGetValue(id, out TimeZoneInfo cached)) return cached;
            TimeZoneInfo zone = ZoneResolver.Find(id, _finder);
            _cache[id] = zone;
            return zone;
        }

        private string SafeFormat(DateTime value, string format, string fallback, List<string> problems)
        {
            try
            {
                // A one-character format is a .NET "standard" pattern; "%" makes custom specifiers like "H" work as written.
                string f = string.IsNullOrEmpty(format) ? fallback : format.Length == 1 ? "%" + format : format;
                return value.ToString(f, _culture);
            }
            catch (FormatException)
            {
                problems.Add("Bad time format '" + format + "'.");
                return value.ToString(fallback, _culture);
            }
        }
    }
}
