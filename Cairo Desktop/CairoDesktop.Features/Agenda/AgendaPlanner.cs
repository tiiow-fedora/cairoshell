using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CairoDesktop.Features.Agenda
{
    public sealed class AgendaItem
    {
        public AgendaItem(string title, DateTimeOffset start, DateTimeOffset end, bool allDay)
        {
            Title = title;
            Start = start;
            End = end;
            AllDay = allDay;
        }

        public string Title { get; }
        public DateTimeOffset Start { get; }
        public DateTimeOffset End { get; }
        public bool AllDay { get; }
    }

    public sealed class AgendaOptions
    {
        public int HorizonDays { get; set; } = 7;
        public int MaxItems { get; set; } = 12;
        public int MaxTextLength { get; set; } = 40;
        public string TimeFormat { get; set; } = "HH:mm";
        public string Label { get; set; } = "Agenda";

        public IList<string> Normalize()
        {
            var findings = new List<string>();
            if (HorizonDays < 1 || HorizonDays > 60) { findings.Add("horizonDays: " + HorizonDays + " is outside 1..60; using 7."); HorizonDays = 7; }
            if (MaxItems < 1 || MaxItems > 50) { findings.Add("maxItems: " + MaxItems + " is outside 1..50; using 12."); MaxItems = 12; }
            if (MaxTextLength < 8 || MaxTextLength > 200) { findings.Add("maxTextLength: " + MaxTextLength + " is outside 8..200; using 40."); MaxTextLength = 40; }
            if (string.IsNullOrWhiteSpace(TimeFormat)) TimeFormat = "HH:mm";
            if (string.IsNullOrWhiteSpace(Label)) Label = "Agenda";
            return findings;
        }
    }

    /// <summary>Expands events into occurrences in a window and formats the widget text. Pure: pass <c>now</c> and the local zone.</summary>
    public sealed class AgendaPlanner
    {
        private const int MaxOccurrencesPerEvent = 2000;
        private readonly IList<IcsEvent> _events;
        private readonly TimeZoneInfo _local;
        private readonly AgendaOptions _options;
        private readonly CultureInfo _culture;

        public AgendaPlanner(IList<IcsEvent> events, TimeZoneInfo localZone, AgendaOptions options, CultureInfo culture)
        {
            _events = events ?? new List<IcsEvent>();
            _local = localZone ?? throw new ArgumentNullException(nameof(localZone));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _culture = culture ?? CultureInfo.InvariantCulture;
        }

        /// <summary>Everything that has not ended by <paramref name="now"/> and starts before now + horizon, soonest first, at most MaxItems.</summary>
        public IList<AgendaItem> Upcoming(DateTimeOffset now)
        {
            DateTimeOffset until = now.AddDays(_options.HorizonDays);
            var items = new List<AgendaItem>();
            foreach (IcsEvent e in _events) Expand(e, now, until, items);
            return items.OrderBy(i => i.Start).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).Take(_options.MaxItems).ToList();
        }

        private void Expand(IcsEvent e, DateTimeOffset now, DateTimeOffset until, List<AgendaItem> into)
        {
            RecurrenceRule r = e.Rule;
            int produced = 0;
            for (int n = 0; n < MaxOccurrencesPerEvent; n++)
            {
                DateTime wall;
                if (r == null)
                {
                    if (n > 0) return;
                    wall = e.WallStart;
                }
                else if (!TryNth(e, r, n, out wall)) return;

                if (r != null && r.Until.HasValue && wall > r.Until.Value) return;
                produced++;
                if (r != null && r.Count.HasValue && produced > r.Count.Value) return;

                // All-day events are dates, not instants: they sit on their own calendar day in the local zone.
                DateTimeOffset start = e.AllDay ? IcsParser.Offset(wall, _local) : TimeZoneInfo.ConvertTime(IcsParser.Offset(wall, e.Zone), _local);
                if (start >= until) return;
                DateTimeOffset end = start + e.Duration;
                bool wanted = e.Duration == TimeSpan.Zero ? start >= now : end > now;
                if (!wanted) continue;
                into.Add(new AgendaItem(e.Summary, start, end, e.AllDay));
            }
        }

        // The n-th start of a recurring event as a wall-clock time. Weekly with BYDAY visits the listed days of each interval-th week.
        private static bool TryNth(IcsEvent e, RecurrenceRule r, int n, out DateTime wall)
        {
            wall = default;
            try
            {
                switch (r.Freq)
                {
                    case RecurrenceFreq.Daily: wall = e.WallStart.AddDays((double)n * r.Interval); return true;
                    case RecurrenceFreq.Monthly: wall = e.WallStart.AddMonths(n * r.Interval); return true;
                    case RecurrenceFreq.Yearly: wall = e.WallStart.AddYears(n * r.Interval); return true;
                    case RecurrenceFreq.Weekly:
                        if (r.Days.Count == 0) { wall = e.WallStart.AddDays(7.0 * n * r.Interval); return true; }
                        var days = r.Days.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList(); // Monday first
                        DateTime weekStart = e.WallStart.Date.AddDays(-(((int)e.WallStart.DayOfWeek + 6) % 7));
                        int produced = 0;
                        for (int week = 0; week < 5000; week++)
                        {
                            foreach (DayOfWeek d in days)
                            {
                                DateTime candidate = weekStart.AddDays(7.0 * week * r.Interval + (((int)d + 6) % 7)).Add(e.WallStart.TimeOfDay);
                                if (candidate < e.WallStart) continue;
                                if (produced++ == n) { wall = candidate; return true; }
                            }
                        }

                        return false;
                    default: return false;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return false; // ran past year 9999
            }
        }

        /// <summary>
        /// "Next 14:30 Standup", "Now Standup" while one is running, "Today Holiday" for an all-day event with nothing timed left,
        /// "Next Tue 09:00 Review" when it is not today, and the label alone when nothing is coming up.
        /// </summary>
        public string FormatText(DateTimeOffset now)
        {
            IList<AgendaItem> items = Upcoming(now);
            DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, _local);
            AgendaItem running = items.FirstOrDefault(i => !i.AllDay && i.Start <= now && i.End > now);
            if (running != null) return Truncate("Now " + running.Title);
            AgendaItem next = items.FirstOrDefault(i => !i.AllDay && i.Start > now);
            if (next != null)
            {
                string when = next.Start.Date == localNow.Date
                    ? next.Start.ToString(_options.TimeFormat, _culture)
                    : next.Start.ToString("ddd", _culture) + " " + next.Start.ToString(_options.TimeFormat, _culture);
                return Truncate("Next " + when + " " + next.Title);
            }

            AgendaItem today = items.FirstOrDefault(i => i.AllDay && i.Start.Date <= localNow.Date && i.End > now);
            if (today != null) return Truncate("Today " + today.Title);
            return _options.Label;
        }

        /// <summary>Upcoming items grouped by day, one line each.</summary>
        public string FormatToolTip(DateTimeOffset now)
        {
            IList<AgendaItem> items = Upcoming(now);
            if (items.Count == 0) return "Nothing in the next " + _options.HorizonDays + " days.";
            var sb = new StringBuilder();
            DateTime? day = null;
            foreach (AgendaItem i in items)
            {
                DateTime d = i.Start.Date;
                if (day != d)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(d.ToString("ddd d MMM", _culture));
                    day = d;
                }

                sb.Append("\n  ").Append(i.AllDay ? "all day" : i.Start.ToString(_options.TimeFormat, _culture)).Append("  ").Append(i.Title);
            }

            return sb.ToString();
        }

        private string Truncate(string text) =>
            text.Length <= _options.MaxTextLength ? text : text.Substring(0, _options.MaxTextLength - 1).TrimEnd() + "…";
    }
}
