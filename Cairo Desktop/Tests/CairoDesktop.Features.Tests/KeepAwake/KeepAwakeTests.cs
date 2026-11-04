using System;
using System.Collections.Generic;
using CairoDesktop.Features.KeepAwake;
using Xunit;

namespace CairoDesktop.Features.Tests.KeepAwake
{
    public class KeepAwakeTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero);

        private static KeepAwakeModel Make() => new KeepAwakeModel(new KeepAwakeOptions());

        [Fact]
        public void Starts_off()
        {
            var m = Make();
            Assert.False(m.IsActive(T0));
            Assert.Equal("Awake: off", m.FormatText(T0));
            Assert.Null(m.RemainingOrNull(T0));
        }

        [Fact]
        public void Clicking_steps_through_presets_then_off()
        {
            var m = Make();
            m.Cycle(T0);
            Assert.Equal("Awake 0:30", m.FormatText(T0));
            m.Cycle(T0);
            Assert.Equal("Awake 1:00", m.FormatText(T0));
            m.Cycle(T0);
            Assert.Equal("Awake 2:00", m.FormatText(T0));
            m.Cycle(T0);
            Assert.Equal("Awake: on", m.FormatText(T0));
            Assert.True(m.IsActive(T0.AddDays(30)));
            m.Cycle(T0);
            Assert.Equal("Awake: off", m.FormatText(T0));
            Assert.False(m.IsActive(T0));
        }

        [Fact]
        public void Timed_state_counts_down_rounding_minutes_up()
        {
            var m = Make();
            m.Cycle(T0);
            Assert.Equal("Awake 0:30", m.FormatText(T0.AddSeconds(1)));
            Assert.Equal("Awake 0:01", m.FormatText(T0.AddMinutes(29).AddSeconds(1)));
            Assert.True(m.IsActive(T0.AddMinutes(29)));
        }

        [Fact]
        public void Timed_state_expires_once_and_returns_to_off()
        {
            var m = Make();
            m.Cycle(T0);
            Assert.False(m.Tick(T0.AddMinutes(29)));
            Assert.False(m.IsActive(T0.AddMinutes(30)));
            Assert.True(m.Tick(T0.AddMinutes(30)));
            Assert.False(m.IsOn);
            Assert.False(m.Tick(T0.AddMinutes(31)));
            Assert.Equal("Awake: off", m.FormatText(T0.AddMinutes(31)));
        }

        [Fact]
        public void Indefinite_state_never_expires()
        {
            var m = Make();
            m.TurnOn(0, T0);
            Assert.False(m.Tick(T0.AddYears(1)));
            Assert.True(m.IsActive(T0.AddYears(1)));
        }

        [Fact]
        public void Clock_moved_backwards_cannot_extend_the_time()
        {
            var m = Make();
            m.Cycle(T0);
            Assert.Equal(TimeSpan.FromMinutes(30), m.RemainingOrNull(T0.AddHours(-5)));
        }

        [Fact]
        public void TurnOn_with_a_custom_length_works_and_clamps()
        {
            var m = Make();
            m.TurnOn(45, T0);
            Assert.Equal("Awake 0:45", m.FormatText(T0));
            m.TurnOn(999999, T0);
            Assert.Equal(TimeSpan.FromMinutes(10080), m.RemainingOrNull(T0));
        }

        [Fact]
        public void Custom_presets_label_and_display_flag()
        {
            var m = new KeepAwakeModel(new KeepAwakeOptions { PresetMinutes = new List<int> { 15, 90 }, Label = "Caffeine", KeepDisplayOn = true });
            Assert.True(m.KeepDisplayOn);
            m.Cycle(T0);
            Assert.Equal("Caffeine 0:15", m.FormatText(T0));
            m.Cycle(T0);
            Assert.Equal("Caffeine 1:30", m.FormatText(T0));
            m.Cycle(T0);
            Assert.Equal("Caffeine: on", m.FormatText(T0));
            m.Cycle(T0);
            Assert.False(m.IsOn);
        }

        [Fact]
        public void Normalize_drops_bad_presets_dedupes_and_always_ends_with_until_off()
        {
            var o = new KeepAwakeOptions { PresetMinutes = new List<int> { -5, 60, 60, 20000, 0, 30 } };
            IList<string> findings = o.Normalize();
            Assert.Equal(new[] { 60, 30, 0 }, o.PresetMinutes);
            Assert.Equal(2, findings.Count);

            var empty = new KeepAwakeOptions { PresetMinutes = new List<int>() };
            Assert.Empty(empty.Normalize());
            Assert.Equal(new[] { 30, 60, 120, 0 }, empty.PresetMinutes);

            var allBad = new KeepAwakeOptions { PresetMinutes = new List<int> { -1 } };
            Assert.Contains(allBad.Normalize(), x => x.Contains("no usable value"));
            Assert.Equal(new[] { 30, 60, 120, 0 }, allBad.PresetMinutes);

            var nullList = new KeepAwakeOptions { PresetMinutes = null, Label = " " };
            nullList.Normalize();
            Assert.Equal("Awake", nullList.Label);
            Assert.Equal(4, nullList.PresetMinutes.Count);
        }

        [Fact]
        public void Tooltip_describes_the_next_click()
        {
            var m = Make();
            Assert.Contains("30 minutes", m.FormatToolTip(T0));
            m.Cycle(T0);
            Assert.Contains("1 hour", m.FormatToolTip(T0));
            m.Cycle(T0);
            m.Cycle(T0);
            Assert.Contains("as long as needed", m.FormatToolTip(T0));
            m.Cycle(T0);
            Assert.Contains("until you turn it off", m.FormatToolTip(T0));
            Assert.Contains("click for off", m.FormatToolTip(T0).ToLowerInvariant());
        }
    }
}
