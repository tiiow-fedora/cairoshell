using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CairoDesktop.Features.WorldClock;

namespace CairoDesktop.Features.Agenda
{
    public enum RecurrenceFreq { None, Daily, Weekly, Monthly, Yearly }

    public sealed class RecurrenceRule
    {
        public RecurrenceFreq Freq { get; set; }
        public int Interval { get; set; } = 1;
        public int? Count { get; set; }
        /// <summary>Last allowed start, as a wall-clock time in the event's zone.</summary>
        public DateTime? Until { get; set; }
        /// <summary>Weekly only: days of the week the event falls on. Empty = the start's weekday.</summary>
        public IList<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();
    }

    /// <summary>One VEVENT. <see cref="WallStart"/> is the clock time in <see cref="Zone"/>; recurrences repeat the wall-clock time across DST.</summary>
    public sealed class IcsEvent
    {
        public string Summary { get; set; }
        public DateTime WallStart { get; set; }
        public TimeZoneInfo Zone { get; set; }
        public TimeSpan Duration { get; set; }
        public bool AllDay { get; set; }
        public RecurrenceRule Rule { get; set; }
    }

    public sealed class IcsResult
    {
        public IList<IcsEvent> Events { get; } = new List<IcsEvent>();
        /// <summary>Unreadable dates, unknown time zones, recurrence rules that are not supported. The rest still loads.</summary>
        public IList<string> Problems { get; } = new List<string>();
    }

    /// <summary>
    /// Small iCalendar reader: VEVENT with SUMMARY, DTSTART, DTEND, STATUS and a simple RRULE (DAILY, WEEKLY with BYDAY,
    /// MONTHLY, YEARLY; INTERVAL, COUNT, UNTIL). Not supported: EXDATE, RDATE, BYMONTHDAY, BYSETPOS and the like (the rule is ignored and the
    /// event shows once). Pure: the caller passes the local zone and a zone finder.
    /// </summary>
    public static class IcsParser
    {
        private sealed class Prop
        {
            public Dictionary<string, string> Parameters;
            public string Value;
        }

        public static IcsResult Parse(string text, TimeZoneInfo localZone, Func<string, TimeZoneInfo> finder)
        {
            var result = new IcsResult();
            if (string.IsNullOrWhiteSpace(text)) return result;
            Dictionary<string, Prop> current = null;
            foreach (string line in Unfold(text))
            {
                string upper = line.Trim().ToUpperInvariant();
                if (upper == "BEGIN:VEVENT") { current = new Dictionary<string, Prop>(); continue; }
                if (upper == "END:VEVENT")
                {
                    if (current != null) Build(current, localZone, finder, result);
                    current = null;
                    continue;
                }

                if (current == null) continue;
                int colon = IndexOfValueColon(line);
                if (colon <= 0) continue;
                string[] head = line.Substring(0, colon).Split(';');
                string name = head[0].Trim().ToUpperInvariant();
                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in head.Skip(1))
                {
                    int eq = p.IndexOf('=');
                    if (eq > 0) parameters[p.Substring(0, eq).Trim()] = p.Substring(eq + 1).Trim().Trim('"');
                }

                if (!current.ContainsKey(name)) current[name] = new Prop { Parameters = parameters, Value = line.Substring(colon + 1) };
            }

            return result;
        }

        private static IEnumerable<string> Unfold(string text)
        {
            var sb = new StringBuilder();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) { sb.Append(line.Substring(1)); continue; }
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear();
                sb.Append(line);
            }

            if (sb.Length > 0) yield return sb.ToString();
        }

        // The first colon outside a quoted parameter value.
        private static int IndexOfValueColon(string line)
        {
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (line[i] == ':' && !quoted) return i;
            }

            return -1;
        }

        private static void Build(Dictionary<string, Prop> props, TimeZoneInfo local, Func<string, TimeZoneInfo> finder, IcsResult result)
        {
            string summary = props.TryGetValue("SUMMARY", out Prop s) ? Unescape(s.Value) : "(no title)";
            if (props.TryGetValue("STATUS", out Prop status) && status.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
            if (!props.TryGetValue("DTSTART", out Prop start))
            {
                result.Problems.Add("'" + summary + "': no DTSTART, skipped.");
                return;
            }

            if (!TryParseTime(start, local, finder, out DateTime wall, out TimeZoneInfo zone, out bool allDay, out string problem))
            {
                result.Problems.Add("'" + summary + "': " + problem + " Skipped.");
                return;
            }

            TimeSpan duration = allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            if (props.TryGetValue("DTEND", out Prop end) &&
                TryParseTime(end, local, finder, out DateTime endWall, out TimeZoneInfo endZone, out _, out _))
            {
                TimeSpan d = Offset(endWall, endZone) - Offset(wall, zone);
                if (d >= TimeSpan.Zero) duration = d;
            }

            RecurrenceRule rule = null;
            if (props.TryGetValue("RRULE", out Prop rr))
            {
                rule = ParseRule(rr.Value, zone, out string ruleProblem);
                if (ruleProblem != null) result.Problems.Add("'" + summary + "': " + ruleProblem);
            }

            result.Events.Add(new IcsEvent { Summary = summary, WallStart = wall, Zone = zone, Duration = duration, AllDay = allDay, Rule = rule });
        }

        internal static DateTimeOffset Offset(DateTime wall, TimeZoneInfo zone)
        {
            DateTime unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
        }

        private static bool TryParseTime(Prop prop, TimeZoneInfo local, Func<string, TimeZoneInfo> finder,
            out DateTime wall, out TimeZoneInfo zone, out bool allDay, out string problem)
        {
            wall = default; zone = local; allDay = false; problem = null;
            string v = prop.Value.Trim();
            bool dateOnly = (prop.Parameters.TryGetValue("VALUE", out string kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase)) || v.Length == 8;
            if (dateOnly)
            {
                if (!DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out wall))
                {
                    problem = "date '" + v + "' is not readable.";
                    return false;
                }

                allDay = true;
                return true;
            }

            bool utc = v.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            string digits = utc ? v.Substring(0, v.Length - 1) : v;
            if (!DateTime.TryParseExact(digits, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out wall))
            {
                problem = "time '" + v + "' is not readable.";
                return false;
            }

            if (utc) { zone = TimeZoneInfo.Utc; return true; }
            if (prop.Parameters.TryGetValue("TZID", out string tzid))
            {
                TimeZoneInfo found = ZoneResolver.Find(tzid, finder);
                if (found == null)
                {
                    problem = "time zone '" + tzid + "' is unknown on this PC.";
                    return false;
                }

                zone = found;
            }

            return true;
        }

        private static RecurrenceRule ParseRule(string text, TimeZoneInfo zone, out string problem)
        {
            problem = null;
            var rule = new RecurrenceRule();
            foreach (string part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string val = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "FREQ":
                        switch (val.ToUpperInvariant())
                        {
                            case "DAILY": rule.Freq = RecurrenceFreq.Daily; break;
                            case "WEEKLY": rule.Freq = RecurrenceFreq.Weekly; break;
                            case "MONTHLY": rule.Freq = RecurrenceFreq.Monthly; break;
                            case "YEARLY": rule.Freq = RecurrenceFreq.Yearly; break;
                            default: problem = "repeat rule FREQ=" + val + " is not supported; shown once."; return null;
                        }

                        break;
                    case "INTERVAL":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval) && interval >= 1) rule.Interval = interval;
                        break;
                    case "COUNT":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count >= 1) rule.Count = count;
                        break;
                    case "UNTIL":
                        rule.Until = ParseUntil(val, zone);
                        break;
                    case "BYDAY":
                        foreach (string d in val.Split(','))
                        {
                            DayOfWeek? dow = ParseDay(d.Trim());
                            if (dow == null) { problem = "repeat rule BYDAY=" + val + " is not supported; shown once."; return null; }
                            rule.Days.Add(dow.Value);
                        }

                        break;
                    case "WKST":
                        break;
                    default:
                        problem = "repeat rule part " + key + " is not supported; shown once.";
                        return null;
                }
            }

            if (rule.Freq == RecurrenceFreq.None) { problem = "repeat rule has no FREQ; shown once."; return null; }
            if (rule.Days.Count > 0 && rule.Freq != RecurrenceFreq.Weekly) { problem = "BYDAY is only supported with FREQ=WEEKLY; shown once."; return null; }
            return rule;
        }

        private static DateTime? ParseUntil(string v, TimeZoneInfo zone)
        {
            if (DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                return d.AddDays(1).AddTicks(-1); // the whole last day
            bool utc = v.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            string digits = utc ? v.Substring(0, v.Length - 1) : v;
            if (!DateTime.TryParseExact(digits, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)) return null;
            return utc ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), zone) : t;
        }

        private static DayOfWeek? ParseDay(string code)
        {
            switch (code.ToUpperInvariant())
            {
                case "MO": return DayOfWeek.Monday;
                case "TU": return DayOfWeek.Tuesday;
                case "WE": return DayOfWeek.Wednesday;
                case "TH": return DayOfWeek.Thursday;
                case "FR": return DayOfWeek.Friday;
                case "SA": return DayOfWeek.Saturday;
                case "SU": return DayOfWeek.Sunday;
                default: return null; // also rejects "1MO" style ordinals
            }
        }

        private static string Unescape(string v)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char n = v[++i];
                    sb.Append(n == 'n' || n == 'N' ? ' ' : n);
                }
                else sb.Append(v[i]);
            }

            return sb.ToString().Trim();
        }
    }
}
