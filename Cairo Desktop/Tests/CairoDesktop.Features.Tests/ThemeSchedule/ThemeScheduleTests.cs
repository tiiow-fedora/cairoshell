using System;
using CairoDesktop.Features.ThemeSchedule;
using Xunit;

namespace CairoDesktop.Features.Tests.ThemeSchedule
{
    public class SolarCalculatorTests
    {
        private static void Near(DateTime expectedUtc, DateTime actualUtc, int minutes = 4)
            => Assert.True(Math.Abs((expectedUtc - actualUtc).TotalMinutes) <= minutes, $"expected {expectedUtc:O} got {actualUtc:O}");

        [Fact]
        public void London_summer_solstice()
        {
            SunTimes t = SolarCalculator.Calculate(new DateTime(2024, 6, 21), 51.5074, -0.1278);
            Assert.Equal(SunCondition.Normal, t.Condition);
            Near(new DateTime(2024, 6, 21, 3, 43, 0), t.SunriseUtc);
            Near(new DateTime(2024, 6, 21, 20, 21, 0), t.SunsetUtc);
        }

        [Fact]
        public void London_winter_solstice()
        {
            SunTimes t = SolarCalculator.Calculate(new DateTime(2024, 12, 21), 51.5074, -0.1278);
            Near(new DateTime(2024, 12, 21, 8, 4, 0), t.SunriseUtc);
            Near(new DateTime(2024, 12, 21, 15, 53, 0), t.SunsetUtc);
        }

        [Fact]
        public void New_York_equinox()
        {
            SunTimes t = SolarCalculator.Calculate(new DateTime(2024, 3, 20), 40.7128, -74.0060);
            Near(new DateTime(2024, 3, 20, 10, 57, 0), t.SunriseUtc);
            Near(new DateTime(2024, 3, 20, 23, 7, 0), t.SunsetUtc);
        }

        [Fact]
        public void Sydney_summer_sunrise_falls_on_the_previous_utc_day()
        {
            SunTimes t = SolarCalculator.Calculate(new DateTime(2024, 12, 21), -33.8688, 151.2093);
            Near(new DateTime(2024, 12, 20, 18, 41, 0), t.SunriseUtc);
            Near(new DateTime(2024, 12, 21, 9, 5, 0), t.SunsetUtc);
        }

        [Fact]
        public void Polar_day_and_night_above_the_arctic_circle()
        {
            Assert.Equal(SunCondition.PolarDay, SolarCalculator.Calculate(new DateTime(2024, 6, 21), 69.65, 18.96).Condition);
            Assert.Equal(SunCondition.PolarNight, SolarCalculator.Calculate(new DateTime(2024, 12, 21), 69.65, 18.96).Condition);
        }

        [Fact]
        public void Equator_has_about_twelve_hours_of_daylight()
        {
            SunTimes t = SolarCalculator.Calculate(new DateTime(2024, 3, 20), 0, 0);
            Assert.InRange((t.SunsetUtc - t.SunriseUtc).TotalMinutes, 710, 730);
        }

        [Fact]
        public void The_poles_do_not_throw()
        {
            SolarCalculator.Calculate(new DateTime(2024, 6, 21), 90, 0);
            SolarCalculator.Calculate(new DateTime(2024, 6, 21), -90, 180);
        }
    }

    public class AppearanceScheduleTests
    {
        private static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");

        private static AppearanceSchedule Fixed(string light = "07:00", string dark = "19:00", TimeZoneInfo zone = null)
        {
            ScheduleConfigParser.TryParseTime(light, out TimeSpan l);
            ScheduleConfigParser.TryParseTime(dark, out TimeSpan d);
            return new AppearanceSchedule(new ScheduleConfig { Enabled = true, Mode = ScheduleMode.Fixed, LightFrom = l, DarkFrom = d }, zone ?? Plus2);
        }

        private static DateTimeOffset Local(int h, int m = 0, int day = 10) => new DateTimeOffset(2026, 5, day, h, m, 0, TimeSpan.FromHours(2));

        [Theory]
        [InlineData(6, 59, false)]
        [InlineData(7, 0, true)]
        [InlineData(12, 0, true)]
        [InlineData(18, 59, true)]
        [InlineData(19, 0, false)]
        [InlineData(23, 59, false)]
        [InlineData(0, 0, false)]
        public void Fixed_times_pick_light_or_dark(int h, int m, bool light)
            => Assert.Equal(light, Fixed().Decide(Local(h, m)).IsLight);

        [Fact]
        public void Next_change_is_the_next_boundary()
        {
            Assert.Equal(Local(19), Fixed().Decide(Local(12)).NextChange);
            Assert.Equal(Local(7, 0, 11), Fixed().Decide(Local(21)).NextChange);
            Assert.Equal(Local(7), Fixed().Decide(Local(3)).NextChange);
        }

        [Fact]
        public void The_instant_of_a_change_belongs_to_the_new_state()
        {
            var d = Fixed().Decide(Local(19));
            Assert.False(d.IsLight);
            Assert.Equal(Local(7, 0, 11), d.NextChange);
        }

        [Fact]
        public void Night_shift_schedule_with_light_after_dark_in_the_clock_works()
        {
            AppearanceSchedule s = Fixed(light: "20:00", dark: "06:00");
            Assert.True(s.Decide(Local(23)).IsLight);
            Assert.True(s.Decide(Local(2)).IsLight);
            Assert.False(s.Decide(Local(6)).IsLight);
            Assert.False(s.Decide(Local(12)).IsLight);
        }

        [Fact]
        public void The_answer_depends_on_the_zone_not_on_the_offset_of_now()
        {
            // 12:00 UTC is 14:00 in +2 (light) but 04:00 in -8 (dark).
            var utcNoon = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            Assert.True(Fixed().Decide(utcNoon).IsLight);
            TimeZoneInfo minus8 = TimeZoneInfo.CreateCustomTimeZone("test-8", TimeSpan.FromHours(-8), "test-8", "test-8");
            Assert.False(Fixed(zone: minus8).Decide(utcNoon).IsLight);
        }

        [Fact]
        public void Daylight_saving_gap_and_overlap_do_not_throw_and_keep_order()
        {
            TimeZoneInfo ny;
            try { ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch (Exception) { return; } // no tz database on this machine
            // 2026-03-08 02:30 does not exist in New York; 2026-11-01 01:30 happens twice.
            AppearanceSchedule gap = Fixed(light: "02:30", dark: "20:00", zone: ny);
            ScheduleDecision d = gap.Decide(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.FromHours(-4)));
            Assert.True(d.IsLight);
            Assert.NotNull(d.NextChange);
            AppearanceSchedule overlap = Fixed(light: "01:30", dark: "20:00", zone: ny);
            ScheduleDecision d2 = overlap.Decide(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5)));
            Assert.True(d2.IsLight);
            Assert.True(d2.NextChange > new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5)));
        }

        private static AppearanceSchedule Sun(double lat, double lon, int riseOffset = 0, int setOffset = 0)
            => new AppearanceSchedule(new ScheduleConfig { Enabled = true, Mode = ScheduleMode.Sun, Latitude = lat, Longitude = lon, SunriseOffsetMinutes = riseOffset, SunsetOffsetMinutes = setOffset }, TimeZoneInfo.Utc);

        [Fact]
        public void Sun_mode_is_light_by_day_and_dark_by_night_in_london()
        {
            AppearanceSchedule s = Sun(51.5074, -0.1278);
            Assert.True(s.Decide(new DateTimeOffset(2024, 6, 21, 12, 0, 0, TimeSpan.Zero)).IsLight);
            Assert.False(s.Decide(new DateTimeOffset(2024, 6, 21, 2, 0, 0, TimeSpan.Zero)).IsLight);
            Assert.False(s.Decide(new DateTimeOffset(2024, 6, 21, 22, 0, 0, TimeSpan.Zero)).IsLight);
            ScheduleDecision noon = s.Decide(new DateTimeOffset(2024, 6, 21, 12, 0, 0, TimeSpan.Zero));
            Assert.InRange((noon.NextChange.Value - new DateTimeOffset(2024, 6, 21, 20, 21, 0, TimeSpan.Zero)).TotalMinutes, -4, 4);
        }

        [Fact]
        public void Sun_mode_across_the_date_line_uses_the_right_day()
        {
            AppearanceSchedule s = Sun(-33.8688, 151.2093); // Sydney, UTC+11 in December
            // 2024-12-20 20:00 UTC = 07:00 local on the 21st: after sunrise (05:41), so light.
            Assert.True(s.Decide(new DateTimeOffset(2024, 12, 20, 20, 0, 0, TimeSpan.Zero)).IsLight);
            // 2024-12-20 17:00 UTC = 04:00 local: before sunrise, dark.
            Assert.False(s.Decide(new DateTimeOffset(2024, 12, 20, 17, 0, 0, TimeSpan.Zero)).IsLight);
        }

        [Fact]
        public void Sun_offsets_shift_the_flip()
        {
            var noonish = new DateTimeOffset(2024, 3, 20, 11, 30, 0, TimeSpan.Zero); // New York sunrise is about 10:57 UTC
            Assert.True(Sun(40.7128, -74.0060).Decide(noonish).IsLight);
            Assert.False(Sun(40.7128, -74.0060, riseOffset: 60).Decide(noonish).IsLight);
        }

        [Fact]
        public void Polar_day_and_night_are_constant_with_no_next_change()
        {
            ScheduleDecision day = Sun(69.65, 18.96).Decide(new DateTimeOffset(2024, 6, 21, 0, 30, 0, TimeSpan.Zero));
            Assert.True(day.IsLight);
            Assert.Null(day.NextChange);
            ScheduleDecision night = Sun(69.65, 18.96).Decide(new DateTimeOffset(2024, 12, 21, 12, 0, 0, TimeSpan.Zero));
            Assert.False(night.IsLight);
            Assert.Null(night.NextChange);
        }

        [Fact]
        public void Sun_mode_without_coordinates_is_refused_by_the_constructor()
            => Assert.Throws<ArgumentException>(() => new AppearanceSchedule(new ScheduleConfig { Mode = ScheduleMode.Sun }, TimeZoneInfo.Utc));
    }

    public class ScheduleConfigTests
    {
        [Fact]
        public void Missing_section_is_off_with_no_findings()
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"theme\": \"Nord\" }");
            Assert.False(r.Config.Enabled);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void Reads_fixed_times_with_comments_and_trailing_commas()
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ // c\n \"themeSchedule\": { \"enabled\": true, \"lightFrom\": \"6:30\", \"darkFrom\": \"21:15\", }, }");
            Assert.True(r.Config.Enabled);
            Assert.Equal(new TimeSpan(6, 30, 0), r.Config.LightFrom);
            Assert.Equal(new TimeSpan(21, 15, 0), r.Config.DarkFrom);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void Sun_mode_without_coordinates_turns_itself_off()
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { \"enabled\": true, \"mode\": \"sun\" } }");
            Assert.False(r.Config.Enabled);
            Assert.Contains(r.Findings, x => x.IsError && x.Message.Contains("latitude"));
        }

        [Fact]
        public void Sun_mode_with_coordinates_and_offsets()
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { \"enabled\": true, \"mode\": \"SUN\", \"latitude\": 51.5, \"longitude\": -0.12, \"sunriseOffsetMinutes\": -30, \"sunsetOffsetMinutes\": 999 } }");
            Assert.True(r.Config.Enabled);
            Assert.Equal(ScheduleMode.Sun, r.Config.Mode);
            Assert.Equal(-30, r.Config.SunriseOffsetMinutes);
            Assert.Equal(180, r.Config.SunsetOffsetMinutes);
            Assert.Single(r.Findings);
            Assert.False(r.Findings[0].IsError);
        }

        [Theory]
        [InlineData("\"latitude\": 91")]
        [InlineData("\"latitude\": \"north\"")]
        [InlineData("\"longitude\": -181")]
        public void Bad_coordinates_are_ignored_with_an_error(string field)
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { " + field + " } }");
            Assert.Null(r.Config.Latitude);
            Assert.Null(r.Config.Longitude);
            Assert.Contains(r.Findings, x => x.IsError);
        }

        [Theory]
        [InlineData("24:00")]
        [InlineData("7pm")]
        [InlineData("7:5")]
        [InlineData("07:00:00")]
        [InlineData("")]
        [InlineData("-1:00")]
        public void Bad_times_fall_back_with_an_error(string time)
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { \"enabled\": true, \"lightFrom\": \"" + time + "\" } }");
            Assert.Equal(TimeSpan.FromHours(7), r.Config.LightFrom);
            Assert.Contains(r.Findings, x => x.IsError && x.Path.EndsWith("lightFrom"));
        }

        [Fact]
        public void Equal_times_are_replaced_with_defaults()
        {
            ScheduleConfigResult r = ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { \"lightFrom\": \"08:00\", \"darkFrom\": \"08:00\" } }");
            Assert.Equal(TimeSpan.FromHours(7), r.Config.LightFrom);
            Assert.Equal(TimeSpan.FromHours(19), r.Config.DarkFrom);
            Assert.Contains(r.Findings, x => x.IsError);
        }

        [Fact]
        public void Wrong_types_unknown_keys_and_broken_json_never_throw()
        {
            Assert.Contains(ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": 5 }").Findings, x => x.IsError);
            Assert.Contains(ScheduleConfigParser.ParseDocument("{ \"themeSchedule\": { \"enabled\": \"yes\", \"mode\": 3, \"bogus\": 1 } }").Findings, x => !x.IsError);
            ScheduleConfigResult broken = ScheduleConfigParser.ParseDocument("{ not json");
            Assert.False(broken.Config.Enabled);
            Assert.Single(broken.Findings);
            Assert.Empty(ScheduleConfigParser.ParseDocument(null).Findings);
        }

        [Theory]
        [InlineData("0:00", true)]
        [InlineData("23:59", true)]
        [InlineData("9:05", true)]
        [InlineData("9:5", false)]
        [InlineData("1:2:3", false)]
        [InlineData(null, false)]
        public void TryParseTime_cases(string text, bool ok) => Assert.Equal(ok, ScheduleConfigParser.TryParseTime(text, out _));
    }
}
