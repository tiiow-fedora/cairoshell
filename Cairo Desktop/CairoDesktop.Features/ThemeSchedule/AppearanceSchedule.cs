using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.ThemeSchedule
{
    /// <summary>What the schedule says at one instant.</summary>
    public sealed class ScheduleDecision
    {
        internal ScheduleDecision(bool isLight, DateTimeOffset? nextChange)
        {
            IsLight = isLight;
            NextChange = nextChange;
        }

        /// <summary>True: use the pack's light version.</summary>
        public bool IsLight { get; }

        /// <summary>When the answer next flips. Null when none is known soon (polar day or night): ask again within a few hours.</summary>
        public DateTimeOffset? NextChange { get; }
    }

    /// <summary>
    /// Decides light or dark from the clock. Fixed mode uses local wall-clock times in the given zone (so DST shifts are honoured);
    /// sun mode uses sunrise and sunset at a place, plus offsets. No clock access: pass <c>now</c> in.
    /// </summary>
    public sealed class AppearanceSchedule
    {
        private readonly ScheduleConfig _config;
        private readonly TimeZoneInfo _zone;

        public AppearanceSchedule(ScheduleConfig config, TimeZoneInfo zone)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _zone = zone ?? throw new ArgumentNullException(nameof(zone));
            if (config.Mode == ScheduleMode.Sun && (config.Latitude == null || config.Longitude == null))
                throw new ArgumentException("Sun mode needs latitude and longitude.", nameof(config));
        }

        public ScheduleDecision Decide(DateTimeOffset now)
            => _config.Mode == ScheduleMode.Sun ? DecideBySun(now) : DecideByClock(now);

        // ---- fixed times ----------------------------------------------------------------------------------------------

        private ScheduleDecision DecideByClock(DateTimeOffset now)
        {
            DateTime localToday = TimeZoneInfo.ConvertTime(now, _zone).Date;
            var events = new List<KeyValuePair<DateTimeOffset, bool>>();
            for (int d = -2; d <= 2; d++)
            {
                DateTime day = localToday.AddDays(d);
                events.Add(new KeyValuePair<DateTimeOffset, bool>(ToInstant(day + _config.LightFrom), true));
                events.Add(new KeyValuePair<DateTimeOffset, bool>(ToInstant(day + _config.DarkFrom), false));
            }

            return Resolve(events, now);
        }

        private DateTimeOffset ToInstant(DateTime wall)
        {
            wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
            if (_zone.IsInvalidTime(wall)) wall = wall.AddHours(1); // spring-forward gap: the time does not exist; take the first moment after it
            return new DateTimeOffset(wall, _zone.GetUtcOffset(wall));
        }

        // ---- sunrise and sunset ---------------------------------------------------------------------------------------

        private ScheduleDecision DecideBySun(DateTimeOffset now)
        {
            DateTime utcDay = now.UtcDateTime.Date;
            var events = new List<KeyValuePair<DateTimeOffset, bool>>();
            SunCondition todayCondition = SunCondition.Normal;
            for (int d = -2; d <= 2; d++)
            {
                SunTimes t = SolarCalculator.Calculate(utcDay.AddDays(d), _config.Latitude.Value, _config.Longitude.Value);
                if (d == 0) todayCondition = t.Condition;
                if (t.Condition != SunCondition.Normal) continue;
                events.Add(new KeyValuePair<DateTimeOffset, bool>(new DateTimeOffset(DateTime.SpecifyKind(t.SunriseUtc, DateTimeKind.Utc)).AddMinutes(_config.SunriseOffsetMinutes), true));
                events.Add(new KeyValuePair<DateTimeOffset, bool>(new DateTimeOffset(DateTime.SpecifyKind(t.SunsetUtc, DateTimeKind.Utc)).AddMinutes(_config.SunsetOffsetMinutes), false));
            }

            if (todayCondition == SunCondition.PolarDay) return new ScheduleDecision(true, null);
            if (todayCondition == SunCondition.PolarNight) return new ScheduleDecision(false, null);
            return Resolve(events, now);
        }

        // ---- shared ---------------------------------------------------------------------------------------------------

        /// <summary>The latest event at or before now decides; the first one after it is the next change.</summary>
        private static ScheduleDecision Resolve(List<KeyValuePair<DateTimeOffset, bool>> events, DateTimeOffset now)
        {
            events.Sort((a, b) => a.Key.CompareTo(b.Key));
            bool? isLight = null;
            DateTimeOffset? next = null;
            foreach (var e in events)
            {
                if (e.Key <= now) isLight = e.Value;
                else
                {
                    // Only a real flip counts as a change (two events in a row with the same value, e.g. after offsets, are one).
                    if (isLight == null || e.Value != isLight.Value) { next = e.Key; break; }
                }
            }

            // Nothing before now in the window (cannot happen with 5 days of events, but never guess): default to light.
            return new ScheduleDecision(isLight ?? true, next);
        }
    }
}
