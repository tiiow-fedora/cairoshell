using System.Linq;
using CairoDesktop.Features.MonitorProfiles;
using Xunit;

namespace CairoDesktop.Features.Tests.MonitorProfiles
{
    public class SelectorTests
    {
        [Fact]
        public void EmptySelector_MatchesEverything()
        {
            Assert.True(new MonitorSelector().Matches(Mon.Fhd()));
            Assert.True(new MonitorSelector().IsEmpty);
        }

        [Fact]
        public void NullMonitor_NeverMatches() => Assert.False(new MonitorSelector().Matches(null));

        [Fact]
        public void Primary_True_MatchesOnlyPrimary()
        {
            var s = new MonitorSelector { Primary = true };
            Assert.True(s.Matches(Mon.Fhd()));
            Assert.False(s.Matches(Mon.Ultra()));
        }

        [Fact]
        public void Primary_False_MatchesSecondary()
        {
            var s = new MonitorSelector { Primary = false };
            Assert.False(s.Matches(Mon.Fhd()));
            Assert.True(s.Matches(Mon.Ultra()));
        }

        [Fact]
        public void Index_MatchesNumberedMonitor()
        {
            var a = Mon.Fhd();
            var b = Mon.Ultra();
            var c = Mon.Portrait();
            Mon.Set(a, b, c);
            var s = new MonitorSelector { Index = 3 };
            Assert.False(s.Matches(a));
            Assert.False(s.Matches(b));
            Assert.True(s.Matches(c));
        }

        [Fact]
        public void Numbering_PrimaryFirst_ThenOrdinalIgnoreCaseNameOrder_LikeMonitorOverrides()
        {
            var p = Mon.Make(@"\\.\DISPLAY3", 0, 0, 1920, 1080, true);
            var d10 = Mon.Make(@"\\.\DISPLAY10", 1920, 0, 1920, 1080);
            var d2 = Mon.Make(@"\\.\DISPLAY2", 3840, 0, 1920, 1080);
            var ordered = MonitorInfo.Number(new[] { d10, p, d2 });
            Assert.Equal(new[] { @"\\.\DISPLAY3", @"\\.\DISPLAY10", @"\\.\DISPLAY2" }, ordered.Select(m => m.DeviceName).ToArray());
            Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(m => m.Index).ToArray());
        }

        [Theory]
        [InlineData("DISPLAY2", true)]
        [InlineData(@"\\.\DISPLAY2", true)]
        [InlineData("display2", true)]
        [InlineData("DISPLAY*", true)]
        [InlineData("*2", true)]
        [InlineData("DISPLAY?", true)]
        [InlineData("DISPLAY1", false)]
        [InlineData("DISP", false)]
        public void DeviceName_Glob(string pattern, bool expected)
        {
            var s = new MonitorSelector { DeviceName = pattern };
            Assert.Equal(expected, s.Matches(Mon.Ultra(@"\\.\DISPLAY2")));
        }

        [Fact]
        public void DeviceName_NullName_DoesNotMatch()
        {
            var m = Mon.Fhd();
            m.DeviceName = null;
            Assert.False(new MonitorSelector { DeviceName = "*" }.Matches(m));
        }

        [Fact]
        public void WidthAndHeightBounds_AreInclusive()
        {
            var m = Mon.Fhd();
            Assert.True(new MonitorSelector { MinWidth = 1920, MaxWidth = 1920, MinHeight = 1080, MaxHeight = 1080 }.Matches(m));
            Assert.False(new MonitorSelector { MinWidth = 1921 }.Matches(m));
            Assert.False(new MonitorSelector { MaxWidth = 1919 }.Matches(m));
            Assert.False(new MonitorSelector { MinHeight = 1081 }.Matches(m));
            Assert.False(new MonitorSelector { MaxHeight = 1079 }.Matches(m));
        }

        [Fact]
        public void Orientation_LandscapePortraitAndSquare()
        {
            var land = new MonitorSelector { Orientation = MonitorOrientation.Landscape };
            var port = new MonitorSelector { Orientation = MonitorOrientation.Portrait };
            var square = Mon.Make("S", 0, 0, 1000, 1000);
            Assert.True(land.Matches(Mon.Fhd()));
            Assert.False(land.Matches(Mon.Portrait()));
            Assert.True(port.Matches(Mon.Portrait()));
            Assert.False(port.Matches(Mon.Fhd()));
            Assert.False(land.Matches(square));
            Assert.False(port.Matches(square));
        }

        [Fact]
        public void AspectRatio_UltrawideThreshold()
        {
            var s = new MonitorSelector { MinAspectRatio = 2.1 };
            Assert.True(s.Matches(Mon.Ultra()));      // 3440x1440 = 2.39
            Assert.False(s.Matches(Mon.Fhd()));       // 1.78
        }

        [Fact]
        public void AspectRatio_ExactBoundaryMatches()
        {
            var m = Mon.Make("X", 0, 0, 2000, 1000);
            Assert.True(new MonitorSelector { MinAspectRatio = 2.0 }.Matches(m));
        }

        [Fact]
        public void AspectRatio_ZeroHeight_DoesNotThrowOrMatch()
        {
            var m = Mon.Make("X", 0, 0, 100, 0);
            Assert.False(new MonitorSelector { MinAspectRatio = 1.0 }.Matches(m));
        }

        [Fact]
        public void Dpi_MinScale()
        {
            var hi = Mon.Make("H", 0, 0, 3840, 2160, false, 1.5);
            var s = new MonitorSelector { MinDpiScale = 1.5 };
            Assert.True(s.Matches(hi));
            Assert.False(s.Matches(Mon.Fhd()));
        }

        [Fact]
        public void Conditions_CombineWithAnd()
        {
            var s = new MonitorSelector { Primary = false, MinWidth = 3000, Orientation = MonitorOrientation.Landscape };
            Assert.True(s.Matches(Mon.Ultra()));
            Assert.False(s.Matches(Mon.Fhd()));
            var primaryUltra = Mon.Make("U", 0, 0, 3440, 1440, true);
            Assert.False(s.Matches(primaryUltra));
        }

        [Fact]
        public void Specificity_ExactDeviceBeatsIndexBeatsGlobBeatsPrimary()
        {
            int exact = new MonitorSelector { DeviceName = "DISPLAY2" }.Specificity;
            int index = new MonitorSelector { Index = 2 }.Specificity;
            int glob = new MonitorSelector { DeviceName = "DISPLAY*" }.Specificity;
            int primary = new MonitorSelector { Primary = true }.Specificity;
            int orient = new MonitorSelector { Orientation = MonitorOrientation.Portrait }.Specificity;
            Assert.True(exact > index);
            Assert.True(index > glob);
            Assert.True(glob > primary);
            Assert.True(primary > orient);
        }

        [Fact]
        public void Specificity_WildcardOnlyGlobCountsAsZero()
        {
            Assert.Equal(0, new MonitorSelector { DeviceName = "*" }.Specificity);
            Assert.Equal(0, new MonitorSelector().Specificity);
        }

        [Fact]
        public void Specificity_LongerGlobIsMoreSpecific()
        {
            Assert.True(new MonitorSelector { DeviceName = @"\\.\DISPLAY*" }.Specificity > new MonitorSelector { DeviceName = "D*" }.Specificity);
        }

        [Fact]
        public void Specificity_AddingConditionsRaisesIt()
        {
            var a = new MonitorSelector { Primary = false };
            var b = new MonitorSelector { Primary = false, MinWidth = 3000 };
            Assert.True(b.Specificity > a.Specificity);
        }

        [Fact]
        public void NeverMatch_Reasons()
        {
            Assert.NotEmpty(new MonitorSelector { MinWidth = 3000, MaxWidth = 1000 }.NeverMatchReasons());
            Assert.NotEmpty(new MonitorSelector { Primary = true, Index = 2 }.NeverMatchReasons());
            Assert.NotEmpty(new MonitorSelector { Primary = false, Index = 1 }.NeverMatchReasons());
            Assert.NotEmpty(new MonitorSelector { Index = 0 }.NeverMatchReasons());
            Assert.NotEmpty(new MonitorSelector { Orientation = MonitorOrientation.Portrait, MinAspectRatio = 1.5 }.NeverMatchReasons());
            Assert.NotEmpty(new MonitorSelector { DeviceName = "  " }.NeverMatchReasons());
            Assert.Empty(new MonitorSelector { Primary = true, MinWidth = 100 }.NeverMatchReasons());
        }

        [Fact]
        public void Covers_BroaderSelectorCoversNarrower()
        {
            var broad = new MonitorSelector { Primary = false };
            var narrow = new MonitorSelector { Primary = false, MinWidth = 3000 };
            Assert.True(broad.Covers(narrow));
            Assert.False(narrow.Covers(broad));
            Assert.True(new MonitorSelector().Covers(narrow));
        }

        [Fact]
        public void Describe_ListsConditions()
        {
            Assert.Equal("every monitor", new MonitorSelector().Describe());
            Assert.Equal("secondary, width >= 3000", new MonitorSelector { Primary = false, MinWidth = 3000 }.Describe());
        }
    }

    public class GlobTests
    {
        [Theory]
        [InlineData("*", "", true)]
        [InlineData("*", "anything", true)]
        [InlineData("", "", true)]
        [InlineData("", "a", false)]
        [InlineData("a*b", "ab", true)]
        [InlineData("a*b", "axxb", true)]
        [InlineData("a*b", "axxbc", false)]
        [InlineData("a**b", "ab", true)]
        [InlineData("a?c", "abc", true)]
        [InlineData("a?c", "ac", false)]
        [InlineData("*a*a*", "banana", true)]
        [InlineData("*a*a*a*a", "banana", false)]
        [InlineData("ABC", "abc", true)]
        [InlineData("?", "", false)]
        [InlineData("a*", "a", true)]
        [InlineData("*b", "abab", true)]
        public void IsMatch(string pattern, string text, bool expected) => Assert.Equal(expected, GlobMatcher.IsMatch(pattern, text));

        [Fact]
        public void NullInputs_DoNotMatch()
        {
            Assert.False(GlobMatcher.IsMatch(null, "a"));
            Assert.False(GlobMatcher.IsMatch("a", null));
        }

        [Fact]
        public void BacktrackingDoesNotExplode()
        {
            string text = new string('a', 5000);
            Assert.False(GlobMatcher.IsMatch("*a*a*a*a*a*b", text));
        }

        [Fact]
        public void LiteralCount_IgnoresWildcards() => Assert.Equal(3, GlobMatcher.LiteralCount("a*b?c"));
    }
}
