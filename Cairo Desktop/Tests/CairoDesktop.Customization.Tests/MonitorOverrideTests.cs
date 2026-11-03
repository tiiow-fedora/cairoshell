using System.Collections.Generic;
using CairoDesktop.Customization.Config;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class MonitorOverrideTests
    {
        private static readonly MonitorInfo Primary = new MonitorInfo { Index = 1, DeviceName = @"\\.\DISPLAY1", IsPrimary = true };
        private static readonly MonitorInfo Second = new MonitorInfo { Index = 2, DeviceName = @"\\.\DISPLAY3", IsPrimary = false };
        private static readonly MonitorInfo Third = new MonitorInfo { Index = 3, DeviceName = @"\\.\DISPLAY2", IsPrimary = false };

        [Theory]
        [InlineData("*", 1)]
        [InlineData("primary", 2)]
        [InlineData("Primary", 2)]
        [InlineData("secondary", 0)]
        [InlineData("1", 3)]
        [InlineData("2", 0)]
        [InlineData("DISPLAY1", 4)]
        [InlineData(@"\\.\DISPLAY1", 4)]
        [InlineData("display1", 4)]
        [InlineData("DISPLAY2", 0)]
        [InlineData("tertiary", 0)]
        [InlineData("", 0)]
        public void Selector_matches_the_primary_monitor(string key, int rank)
        {
            Assert.Equal(rank, MonitorOverrides.Match(key, Primary));
        }

        [Theory]
        [InlineData("secondary", 2)]
        [InlineData("primary", 0)]
        [InlineData("2", 3)]
        [InlineData("DISPLAY3", 4)]
        [InlineData("DISPLAY1", 0)]
        public void Selector_matches_a_secondary_monitor(string key, int rank)
        {
            Assert.Equal(rank, MonitorOverrides.Match(key, Second));
        }

        [Theory]
        [InlineData("*", true)]
        [InlineData("primary", true)]
        [InlineData("SECONDARY", true)]
        [InlineData("3", true)]
        [InlineData("DISPLAY12", true)]
        [InlineData(@"\\.\DISPLAY2", true)]
        [InlineData("0", false)]
        [InlineData("-1", false)]
        [InlineData("left", false)]
        [InlineData(" ", false)]
        public void Selector_validation(string key, bool valid)
        {
            Assert.Equal(valid, MonitorOverrides.ValidateKey(key) == null);
        }

        private static Dictionary<string, MonitorConfig> Parse(string json)
        {
            var result = ConfigLoader.Parse(json);
            Assert.True(result.Success, result.Error);
            return result.Config.Monitors;
        }

        [Fact]
        public void Monitors_section_parses_with_comments()
        {
            var monitors = Parse(@"{
  ""monitors"": {
    // the TV
    ""secondary"": { ""menuBar"": { ""opacity"": 0.5, ""layout"": { ""right"": [""widget:clock""] } }, ""taskbar"": { ""floating"": true } },
    ""\\\\.\\DISPLAY2"": { ""menuBar"": { ""cornerRadius"": 12 } },
  }
}");
            Assert.Equal(2, monitors.Count);
            Assert.Equal(0.5, monitors["secondary"].MenuBar.Opacity);
            Assert.True(monitors["secondary"].Taskbar.Floating);
            Assert.Equal(12, monitors[@"\\.\DISPLAY2"].MenuBar.CornerRadius);
        }

        [Fact]
        public void Overrides_apply_field_by_field_over_the_base()
        {
            var monitors = Parse(@"{ ""monitors"": { ""secondary"": { ""menuBar"": { ""opacity"": 0.5 } } } }");
            var baseBar = new BarConfig { Opacity = 1, CornerRadius = 8, Spacing = 4 };

            var onSecond = MonitorOverrides.Apply(baseBar, monitors, Second, taskbar: false);
            Assert.Equal(0.5, onSecond.Opacity);
            Assert.Equal(8, onSecond.CornerRadius);
            Assert.Equal(4, onSecond.Spacing);

            var onPrimary = MonitorOverrides.Apply(baseBar, monitors, Primary, taskbar: false);
            Assert.Same(baseBar, onPrimary);
            Assert.Equal(1, onPrimary.Opacity);

            Assert.Same(baseBar, MonitorOverrides.Apply(baseBar, monitors, Second, taskbar: true));
        }

        [Fact]
        public void More_specific_selectors_win_whatever_the_file_order()
        {
            var monitors = Parse(@"{ ""monitors"": {
  ""DISPLAY3"":  { ""menuBar"": { ""opacity"": 0.2 } },
  ""2"":         { ""menuBar"": { ""opacity"": 0.4, ""spacing"": 9 } },
  ""secondary"": { ""menuBar"": { ""opacity"": 0.6, ""spacing"": 7, ""cornerRadius"": 5 } },
  ""*"":         { ""menuBar"": { ""opacity"": 0.8, ""spacing"": 1, ""cornerRadius"": 1, ""margin"": 3 } }
} }");

            var bar = MonitorOverrides.Apply(null, monitors, Second, taskbar: false);
            Assert.Equal(0.2, bar.Opacity);
            Assert.Equal(9, bar.Spacing);
            Assert.Equal(5, bar.CornerRadius);
            Assert.Equal(3, bar.Margin);

            // The third monitor is only matched by "secondary" and "*".
            var third = MonitorOverrides.Apply(null, monitors, Third, taskbar: false);
            Assert.Equal(0.6, third.Opacity);
            Assert.Equal(7, third.Spacing);
        }

        [Fact]
        public void A_layout_override_replaces_the_whole_layout()
        {
            var monitors = Parse(@"{ ""monitors"": { ""secondary"": { ""menuBar"": { ""layout"": { ""center"": [""widget:clock""] } } } } }");
            var baseBar = new BarConfig { Layout = new ZoneLayout { Left = new List<string> { "cairoMenu" } } };

            var bar = MonitorOverrides.Apply(baseBar, monitors, Second, taskbar: false);
            Assert.Null(bar.Layout.Left);
            Assert.Equal(new[] { "widget:clock" }, bar.Layout.Center);
        }

        [Fact]
        public void Resolved_settings_expose_per_monitor_bars()
        {
            var config = ConfigLoader.Parse(@"{
  ""enabled"": true,
  ""menuBar"": { ""cornerRadius"": 6 },
  ""monitors"": { ""2"": { ""menuBar"": { ""cornerRadius"": 0 }, ""taskbar"": { ""spacing"": 2 } } }
}").Config;
            var settings = ResolvedSettings.Resolve(config, null);

            Assert.Equal(6, settings.GetBar(false, Primary).CornerRadius);
            Assert.Equal(0, settings.GetBar(false, Second).CornerRadius);
            Assert.Null(settings.GetBar(true, Primary));
            Assert.Equal(2, settings.GetBar(true, Second).Spacing);
            Assert.Null(ResolvedSettings.Disabled.GetBar(false, Second));
        }

        [Fact]
        public void Checker_flags_bad_selectors_and_empty_entries()
        {
            var config = ConfigLoader.Parse(@"{ ""monitors"": { ""left"": { ""menuBar"": { } }, ""primary"": { }, ""2"": { ""taskbar"": { } } } }").Config;
            var problems = ConfigChecker.Check(config);
            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, p => p.Contains("'left'"));
            Assert.Contains(problems, p => p.Contains("'primary'"));
        }
    }
}
