using System;
using System.Collections.Generic;
using System.Globalization;
using CairoDesktop.Features.WorldClock;
using Xunit;

namespace CairoDesktop.Features.Tests.WorldClock
{
    public class WorldClockTests
    {
        private static TimeZoneInfo Zone(string id, int hours, int minutes = 0)
            => TimeZoneInfo.CreateCustomTimeZone(id, new TimeSpan(hours, Math.Sign(hours == 0 ? minutes : hours) * minutes, 0), id, id);

        private static readonly Dictionary<string, TimeZoneInfo> Known = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Asia/Tokyo"] = Zone("Asia/Tokyo", 9),
            ["America/New_York"] = Zone("America/New_York", -5),
            ["Asia/Kolkata"] = Zone("Asia/Kolkata", 5, 30),
            ["UTC"] = TimeZoneInfo.Utc,
            ["GMT Standard Time"] = Zone("GMT Standard Time", 0),
        };

        private static TimeZoneInfo Finder(string id)
        {
            if (Known.TryGetValue(id, out TimeZoneInfo z)) return z;
            throw new TimeZoneNotFoundException(id);
        }

        private static WorldClockFormatter Make(string specs, WorldClockOptions options = null)
            => new WorldClockFormatter(ClockZoneSpec.ParseList(specs), options, Finder, CultureInfo.InvariantCulture);

        // 2026-05-06 is a Wednesday. Local PC zone: UTC+0, 15:00.
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 5, 6, 15, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Renders_labels_times_and_day_deltas()
        {
            WorldClockView v = Make("Tokyo=Asia/Tokyo; NYC=America/New_York").Render(Now, TimeZoneInfo.Utc);
            Assert.Equal("Tokyo 00:00 +1d   NYC 10:00", v.Text);
            Assert.Empty(v.Problems);
        }

        [Fact]
        public void Negative_day_delta_and_half_hour_zone()
        {
            var early = new DateTimeOffset(2026, 5, 6, 2, 0, 0, TimeSpan.Zero);
            WorldClockView v = Make("America/New_York;Asia/Kolkata").Render(early, TimeZoneInfo.Utc);
            Assert.Equal("New York 21:00 -1d   Kolkata 07:30", v.Text);
        }

        [Fact]
        public void Options_hide_labels_and_deltas_and_change_format_and_separator()
        {
            var o = new WorldClockOptions { ShowLabels = false, ShowDayDelta = false, Format = "h:mm tt", Separator = " | " };
            Assert.Equal("12:00 AM | 10:00 AM", Make("Asia/Tokyo;America/New_York", o).Render(Now, TimeZoneInfo.Utc).Text);
        }

        [Fact]
        public void Unknown_zones_are_reported_once_and_skipped()
        {
            WorldClockView v = Make("Nowhere/Land;Asia/Tokyo;Nowhere/Land").Render(Now, TimeZoneInfo.Utc);
            Assert.Equal("Tokyo 00:00 +1d", v.Text);
            Assert.Single(v.Problems);
            Assert.Contains("Nowhere/Land", v.Problems[0]);
        }

        [Fact]
        public void The_other_naming_scheme_is_tried()
        {
            // The machine only knows the Windows id; the user wrote the IANA one.
            WorldClockView v = Make("London=Europe/London").Render(Now, TimeZoneInfo.Utc);
            Assert.Equal("London 15:00", v.Text);
            Assert.Empty(v.Problems);
        }

        [Fact]
        public void Bad_format_falls_back_and_reports()
        {
            var o = new WorldClockOptions { Format = "HH:mm:ss.fffffffff" };
            WorldClockView v = Make("Asia/Tokyo", o).Render(Now, TimeZoneInfo.Utc);
            Assert.Equal("Tokyo 00:00 +1d", v.Text);
            Assert.Contains(v.Problems, p => p.Contains("Bad time format"));
        }

        [Fact]
        public void Single_character_format_is_treated_as_a_custom_pattern()
        {
            var o = new WorldClockOptions { Format = "H", ShowLabels = false };
            Assert.Equal("0 +1d", Make("Asia/Tokyo", o).Render(Now, TimeZoneInfo.Utc).Text);
        }

        [Fact]
        public void Tooltip_has_offset_and_work_hours_marker()
        {
            WorldClockView v = Make("NYC=America/New_York;Tokyo=Asia/Tokyo;Delhi=Asia/Kolkata").Render(Now, TimeZoneInfo.Utc);
            string[] lines = v.ToolTip.Split('\n');
            Assert.Equal("NYC: Wed 6 May  10:00  UTC-05  [work hours]", lines[0]);
            Assert.Equal("Tokyo: Thu 7 May  00:00 (+1 day)  UTC+09", lines[1]);
            Assert.Equal("Delhi: Wed 6 May  20:30  UTC+05:30", lines[2]);
        }

        [Fact]
        public void Weekends_and_edges_of_the_working_day()
        {
            var f = Make("UTC");
            Assert.True(f.IsWorkTime(new DateTime(2026, 5, 6, 9, 0, 0)));
            Assert.False(f.IsWorkTime(new DateTime(2026, 5, 6, 8, 59, 0)));
            Assert.False(f.IsWorkTime(new DateTime(2026, 5, 6, 18, 0, 0)));
            Assert.False(f.IsWorkTime(new DateTime(2026, 5, 9, 12, 0, 0))); // Saturday
            Assert.False(f.IsWorkTime(new DateTime(2026, 5, 10, 12, 0, 0))); // Sunday
        }

        [Fact]
        public void Empty_list_gives_empty_text()
        {
            WorldClockView v = Make("").Render(Now, TimeZoneInfo.Utc);
            Assert.Equal("", v.Text);
            Assert.Equal("", v.ToolTip);
        }

        [Fact]
        public void Local_zone_decides_the_day_delta()
        {
            // The PC is in Tokyo (UTC+9): its date is already the 7th, same as Tokyo, so no delta.
            WorldClockView v = Make("Asia/Tokyo").Render(Now, Known["Asia/Tokyo"]);
            Assert.Equal("Tokyo 00:00", v.Text);
        }

        [Theory]
        [InlineData("Tokyo=Asia/Tokyo", "Tokyo", "Asia/Tokyo")]
        [InlineData("Asia/Tokyo", "Tokyo", "Asia/Tokyo")]
        [InlineData("America/Argentina/Buenos_Aires", "Buenos Aires", "America/Argentina/Buenos_Aires")]
        [InlineData("  Home = UTC ", "Home", "UTC")]
        [InlineData("=UTC", "UTC", "UTC")]
        public void Spec_parsing(string text, string label, string id)
        {
            ClockZoneSpec s = ClockZoneSpec.Parse(text);
            Assert.Equal(label, s.Label);
            Assert.Equal(id, s.ZoneId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Label=")]
        public void Spec_parsing_refuses_blanks(string text) => Assert.Null(ClockZoneSpec.Parse(text));

        [Fact]
        public void List_parsing_accepts_semicolons_commas_and_newlines()
            => Assert.Equal(4, ClockZoneSpec.ParseList("a/b;c/d, e/f\ng/h;;").Count);

        [Theory]
        [InlineData(0, 0, "+00")]
        [InlineData(5, 30, "+05:30")]
        [InlineData(-5, 0, "-05")]
        [InlineData(-9, -30, "-09:30")]
        public void Offset_formatting(int h, int m, string expected)
            => Assert.Equal(expected, WorldClockFormatter.FormatOffset(new TimeSpan(h, m, 0)));

        [Fact]
        public void Resolver_returns_null_for_blank_or_unknown_and_does_not_throw()
        {
            Assert.Null(ZoneResolver.Find(null, Finder));
            Assert.Null(ZoneResolver.Find("Mars/Olympus", Finder));
            Assert.Null(ZoneResolver.Find("Asia/Tokyo", id => null));
            Assert.Null(ZoneResolver.Find("Asia/Tokyo", id => throw new InvalidTimeZoneException()));
        }

        [Fact]
        public void Real_system_zone_lookup_works_when_the_database_is_present()
        {
            TimeZoneInfo z = ZoneResolver.Find("Europe/London", TimeZoneInfo.FindSystemTimeZoneById);
            if (z == null) return; // machine without a time-zone database
            var summer = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(TimeSpan.FromHours(1), z.GetUtcOffset(summer));
        }
    }
}
