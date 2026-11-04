using System;
using System.Globalization;
using System.Linq;
using CairoDesktop.Features.Agenda;
using Xunit;

namespace CairoDesktop.Features.Tests.Agenda
{
    public class AgendaTests
    {
        private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
        // Tuesday 3 Feb 2026, 10:00 UTC.
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero);

        private static string Ics(params string[] events) =>
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("", events.Select(e => "BEGIN:VEVENT\r\n" + e + "\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

        private static IcsResult Parse(string text) => IcsParser.Parse(text, Utc, id => TimeZoneInfo.FindSystemTimeZoneById(id));

        private static AgendaPlanner Planner(IcsResult r, Action<AgendaOptions> tweak = null)
        {
            var o = new AgendaOptions();
            tweak?.Invoke(o);
            o.Normalize();
            return new AgendaPlanner(r.Events, Utc, o, CultureInfo.InvariantCulture);
        }

        [Fact]
        public void Parses_utc_event_with_end()
        {
            var r = Parse(Ics("SUMMARY:Standup\r\nDTSTART:20260203T143000Z\r\nDTEND:20260203T150000Z"));
            Assert.Empty(r.Problems);
            Assert.Single(r.Events);
            Assert.Equal(TimeSpan.FromMinutes(30), r.Events[0].Duration);
            Assert.Equal("Next 14:30 Standup", Planner(r).FormatText(Now));
        }

        [Fact]
        public void Unfolds_long_lines_and_unescapes()
        {
            var r = Parse(Ics("SUMMARY:Lunch\\, with\r\n  Sam\\; ok\r\nDTSTART:20260203T120000Z"));
            Assert.Equal("Lunch, with Sam; ok", r.Events[0].Summary);
        }

        [Fact]
        public void Floating_time_uses_local_zone_and_tzid_uses_that_zone()
        {
            var floating = Parse(Ics("SUMMARY:A\r\nDTSTART:20260203T130000"));
            Assert.Equal("Next 13:00 A", Planner(floating).FormatText(Now));
            // Fixed-offset zone avoids depending on tz data on the test machine.
            TimeZoneInfo plus2 = TimeZoneInfo.CreateCustomTimeZone("X2", TimeSpan.FromHours(2), "X2", "X2");
            var r = IcsParser.Parse(Ics("SUMMARY:B\r\nDTSTART;TZID=X2:20260203T140000"), Utc, id => id == "X2" ? plus2 : throw new TimeZoneNotFoundException());
            Assert.Equal("Next 12:00 B", Planner(r).FormatText(Now));
        }

        [Fact]
        public void Unknown_tzid_is_a_problem_not_a_crash()
        {
            var r = Parse(Ics("SUMMARY:B\r\nDTSTART;TZID=Nowhere/Land:20260203T140000", "SUMMARY:OK\r\nDTSTART:20260203T150000Z"));
            Assert.Single(r.Events);
            Assert.Contains("Nowhere/Land", r.Problems.Single());
        }

        [Fact]
        public void Bad_dates_and_missing_start_are_reported()
        {
            var r = Parse(Ics("SUMMARY:A\r\nDTSTART:soon", "SUMMARY:B"));
            Assert.Empty(r.Events);
            Assert.Equal(2, r.Problems.Count);
        }

        [Fact]
        public void Cancelled_events_are_skipped()
        {
            var r = Parse(Ics("SUMMARY:A\r\nSTATUS:CANCELLED\r\nDTSTART:20260203T150000Z"));
            Assert.Empty(r.Events);
            Assert.Empty(r.Problems);
        }

        [Fact]
        public void Running_event_shows_now_and_finished_one_is_gone()
        {
            var r = Parse(Ics("SUMMARY:Review\r\nDTSTART:20260203T093000Z\r\nDTEND:20260203T103000Z", "SUMMARY:Old\r\nDTSTART:20260203T080000Z\r\nDTEND:20260203T090000Z"));
            var p = Planner(r);
            Assert.Equal("Now Review", p.FormatText(Now));
            Assert.DoesNotContain("Old", p.FormatToolTip(Now));
        }

        [Fact]
        public void Event_on_another_day_names_the_weekday()
        {
            var r = Parse(Ics("SUMMARY:Review\r\nDTSTART:20260205T090000Z"));
            Assert.Equal("Next Thu 09:00 Review", Planner(r).FormatText(Now));
        }

        [Fact]
        public void Nothing_coming_up_shows_label()
        {
            Assert.Equal("Agenda", Planner(Parse(Ics())).FormatText(Now));
            var far = Parse(Ics("SUMMARY:Later\r\nDTSTART:20260301T090000Z"));
            Assert.Equal("Agenda", Planner(far).FormatText(Now));
            Assert.Contains("Nothing in the next 7 days", Planner(far).FormatToolTip(Now));
        }

        [Fact]
        public void All_day_event_shows_today_when_nothing_timed_is_left()
        {
            var r = Parse(Ics("SUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20260203\r\nDTEND;VALUE=DATE:20260204"));
            var p = Planner(r);
            Assert.Equal("Today Holiday", p.FormatText(Now));
            Assert.Contains("all day  Holiday", p.FormatToolTip(Now));
            var timed = Parse(Ics("SUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20260203", "SUMMARY:Call\r\nDTSTART:20260203T160000Z"));
            Assert.Equal("Next 16:00 Call", Planner(timed).FormatText(Now));
        }

        [Fact]
        public void Daily_recurrence_with_count()
        {
            var r = Parse(Ics("SUMMARY:Daily\r\nDTSTART:20260201T090000Z\r\nRRULE:FREQ=DAILY;COUNT=4")); // 1,2,3,4 Feb
            var items = Planner(r).Upcoming(Now);
            Assert.Single(items);
            Assert.Equal(4, items[0].Start.Day);
        }

        [Fact]
        public void Weekly_byday_visits_each_listed_day_with_interval()
        {
            // Starts Mon 2 Feb; every second week on Mon and Wed: 2 Feb, 4 Feb, 16 Feb, 18 Feb.
            var r = Parse(Ics("SUMMARY:Gym\r\nDTSTART:20260202T180000Z\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE"));
            var days = Planner(r, o => o.HorizonDays = 20).Upcoming(Now).Select(i => i.Start.Day).ToArray();
            Assert.Equal(new[] { 4, 16, 18 }, days);
        }

        [Fact]
        public void Until_stops_the_series_inclusive_of_last_day()
        {
            var r = Parse(Ics("SUMMARY:D\r\nDTSTART:20260201T090000Z\r\nRRULE:FREQ=DAILY;UNTIL=20260205"));
            var days = Planner(r, o => o.HorizonDays = 30).Upcoming(Now).Select(i => i.Start.Day).ToArray();
            Assert.Equal(new[] { 4, 5 }, days);
        }

        [Fact]
        public void Monthly_and_yearly_step_the_calendar()
        {
            var m = Parse(Ics("SUMMARY:Rent\r\nDTSTART:20251201T090000Z\r\nRRULE:FREQ=MONTHLY"));
            Assert.Equal(new DateTime(2026, 3, 1), Planner(m, o => o.HorizonDays = 30).Upcoming(Now).Single().Start.Date);
            var y = Parse(Ics("SUMMARY:Bday\r\nDTSTART;VALUE=DATE:20200205\r\nRRULE:FREQ=YEARLY"));
            Assert.Equal(new DateTime(2026, 2, 5), Planner(y).Upcoming(Now).Single().Start.Date);
        }

        [Fact]
        public void Unsupported_rules_show_once_and_report()
        {
            var r = Parse(Ics("SUMMARY:X\r\nDTSTART:20260204T090000Z\r\nRRULE:FREQ=MONTHLY;BYMONTHDAY=15"));
            Assert.Single(r.Problems);
            Assert.Single(Planner(r, o => o.HorizonDays = 60).Upcoming(Now));
        }

        [Fact]
        public void Max_items_and_sorting()
        {
            var r = Parse(Ics("SUMMARY:B\r\nDTSTART:20260203T130000Z", "SUMMARY:A\r\nDTSTART:20260203T120000Z", "SUMMARY:C\r\nDTSTART:20260203T140000Z"));
            var items = Planner(r, o => o.MaxItems = 2).Upcoming(Now);
            Assert.Equal(new[] { "A", "B" }, items.Select(i => i.Title).ToArray());
        }

        [Fact]
        public void Long_title_is_truncated_with_ellipsis()
        {
            var r = Parse(Ics("SUMMARY:" + new string('w', 100) + "\r\nDTSTART:20260203T130000Z"));
            string text = Planner(r, o => o.MaxTextLength = 20).FormatText(Now);
            Assert.Equal(20, text.Length);
            Assert.EndsWith("…", text);
        }

        [Fact]
        public void Empty_or_garbage_input_is_fine()
        {
            Assert.Empty(Parse(null).Events);
            Assert.Empty(Parse("hello world").Events);
        }

        [Fact]
        public void Normalize_clamps()
        {
            var o = new AgendaOptions { HorizonDays = 0, MaxItems = 99, MaxTextLength = 1 };
            Assert.Equal(3, o.Normalize().Count);
            Assert.Equal(7, o.HorizonDays);
        }
    }
}
