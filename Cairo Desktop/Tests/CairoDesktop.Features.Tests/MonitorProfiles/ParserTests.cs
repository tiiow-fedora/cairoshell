using System.Linq;
using CairoDesktop.Features.MonitorProfiles;
using Xunit;

namespace CairoDesktop.Features.Tests.MonitorProfiles
{
    public class ParserTests
    {
        private const string Full = @"{
  // comment
  ""monitorProfiles"": {
    ""default"": { ""height"": 32, ""showClock"": true, },
    ""rules"": [
      { ""name"": ""ultrawide"", ""match"": { ""minAspectRatio"": 2.1 }, ""profile"": { ""floating"": true, ""margin"": 8 } },
      /* block */
      { ""name"": ""side"", ""match"": { ""orientation"": ""portrait"" }, ""profile"": { ""position"": ""Left"", ""themeVariant"": ""dark"" } },
    ],
  },
}";

        private static bool Has(ProfilesParseResult r, FindingSeverity sev, string text) =>
            r.Findings.Any(f => f.Severity == sev && (f.Message.Contains(text) || f.Path.Contains(text)));

        [Fact]
        public void Lenient_CommentsAndTrailingCommas_Parse()
        {
            var r = ProfilesConfigParser.Parse(Full);
            Assert.Empty(r.Findings);
            Assert.Equal(32, r.Config.Default.Height);
            Assert.Equal(2, r.Config.Rules.Count);
            Assert.Equal("ultrawide", r.Config.Rules[0].Name);
            Assert.Equal(2.1, r.Config.Rules[0].Match.MinAspectRatio);
            Assert.Equal(true, r.Config.Rules[0].Settings.Floating);
            Assert.Equal(TaskbarEdge.Left, r.Config.Rules[1].Settings.Position);
            Assert.Equal(ThemeVariant.Dark, r.Config.Rules[1].Settings.ThemeVariant);
            Assert.Equal(MonitorOrientation.Portrait, r.Config.Rules[1].Match.Orientation);
        }

        [Fact]
        public void SectionObject_WithoutWrapper_IsAccepted()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""height"": 40 } }");
            Assert.Empty(r.Findings);
            Assert.Equal(40, r.Config.Default.Height);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyInput_IsEmptyConfig(string json)
        {
            var r = ProfilesConfigParser.Parse(json);
            Assert.Empty(r.Findings);
            Assert.Empty(r.Config.Rules);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[1,2,3]")]
        [InlineData("42")]
        [InlineData("\"str\"")]
        [InlineData("{ \"monitorProfiles\": 5 }")]
        [InlineData("{ \"monitorProfiles\": { \"rules\": 5 } }")]
        [InlineData("{ \"monitorProfiles\": { \"rules\": [1, \"x\", null] } }")]
        [InlineData("{ \"monitorProfiles\": { \"default\": [] } }")]
        [InlineData("{ \"monitorProfiles\": { \"rules\": [ { \"match\": 3, \"profile\": \"x\" } ] } }")]
        public void Garbage_NeverThrows_AndReportsError(string json)
        {
            var r = ProfilesConfigParser.Parse(json);
            Assert.True(r.HasErrors);
        }

        [Fact]
        public void NullSection_IsEmptyConfig()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""monitorProfiles"": null }");
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void UnknownKeys_AreWarnedAtEveryLevel()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""monitorProfiles"": { ""bogus"": 1, ""default"": { ""hieght"": 3 },
                ""rules"": [ { ""name"": ""a"", ""when"": {}, ""match"": { ""colour"": ""red"" }, ""profile"": { ""height"": 30 } } ] } }");
            Assert.True(Has(r, FindingSeverity.Warning, "bogus"));
            Assert.True(Has(r, FindingSeverity.Warning, "hieght"));
            Assert.True(Has(r, FindingSeverity.Warning, "'when'"));
            Assert.True(Has(r, FindingSeverity.Warning, "colour"));
            Assert.False(r.HasErrors);
        }

        [Fact]
        public void KeysAreCaseInsensitive()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""MonitorProfiles"": { ""Default"": { ""Height"": 31, ""AutoHide"": true } } }");
            Assert.Empty(r.Findings);
            Assert.Equal(31, r.Config.Default.Height);
            Assert.Equal(true, r.Config.Default.AutoHide);
        }

        [Fact]
        public void BadEnums_AreErrors_AndValueDropped()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""position"": ""middle"", ""themeVariant"": 3 },
                ""rules"": [ { ""match"": { ""orientation"": ""diagonal"" } } ] }");
            Assert.Equal(3, r.Findings.Count(f => f.Severity == FindingSeverity.Error));
            Assert.Null(r.Config.Default.Position);
            Assert.Null(r.Config.Default.ThemeVariant);
            Assert.Null(r.Config.Rules[0].Match.Orientation);
            Assert.True(Has(r, FindingSeverity.Error, "top, bottom, left, right"));
        }

        [Fact]
        public void EnumsAreCaseInsensitive()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""position"": ""TOP"" } }");
            Assert.Equal(TaskbarEdge.Top, r.Config.Default.Position);
        }

        [Theory]
        [InlineData("height", -5)]
        [InlineData("height", 0)]
        [InlineData("iconSize", -1)]
        [InlineData("iconSize", 0)]
        [InlineData("margin", -3)]
        public void NegativeOrZeroSizes_AreErrors(string key, int value)
        {
            var r = ProfilesConfigParser.Parse("{ \"default\": { \"" + key + "\": " + value + " } }");
            Assert.True(r.HasErrors);
            Assert.Null(r.Config.Default.Get(key));
        }

        [Fact]
        public void ZeroMargin_IsValid()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""margin"": 0 } }");
            Assert.Empty(r.Findings);
            Assert.Equal(0, r.Config.Default.Margin);
        }

        [Fact]
        public void WrongTypes_AreErrors()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""height"": ""tall"", ""autoHide"": ""yes"", ""layoutName"": 5, ""iconSize"": 2.5 } }");
            Assert.Equal(4, r.Findings.Count(f => f.Severity == FindingSeverity.Error));
            Assert.Null(r.Config.Default.Height);
            Assert.Null(r.Config.Default.AutoHide);
        }

        [Fact]
        public void EmptyLayoutName_WarnsAndIsIgnored()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""layoutName"": ""  "" } }");
            Assert.Null(r.Config.Default.LayoutName);
            Assert.True(Has(r, FindingSeverity.Warning, "empty"));
        }

        [Fact]
        public void NullValues_MeanUnset_WithoutFindings()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""default"": { ""height"": null, ""position"": null } }");
            Assert.Empty(r.Findings);
            Assert.Null(r.Config.Default.Height);
        }

        [Fact]
        public void RuleThatCanNeverMatch_IsWarned_ButKept()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [
                { ""name"": ""a"", ""match"": { ""minWidth"": 3000, ""maxWidth"": 1000 }, ""profile"": { ""height"": 20 } },
                { ""name"": ""b"", ""match"": { ""primary"": true, ""index"": 2 }, ""profile"": { ""height"": 20 } },
                { ""name"": ""c"", ""match"": { ""orientation"": ""portrait"", ""minAspectRatio"": 2 }, ""profile"": { ""height"": 20 } } ] }");
            Assert.Equal(3, r.Findings.Count(f => f.Message.Contains("can never match")));
            Assert.Equal(3, r.Config.Rules.Count);
        }

        [Fact]
        public void NegativeSelectorBounds_AreErrors()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [ { ""match"": { ""minWidth"": -10 }, ""profile"": { ""height"": 20 } } ] }");
            Assert.True(r.HasErrors);
        }

        [Fact]
        public void DuplicateRuleNames_AreWarned_CaseInsensitively()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [
                { ""name"": ""Wide"", ""match"": { ""primary"": true }, ""profile"": { ""height"": 20 } },
                { ""name"": ""wide"", ""match"": { ""primary"": false }, ""profile"": { ""height"": 21 } } ] }");
            Assert.True(Has(r, FindingSeverity.Warning, "duplicate rule name"));
        }

        [Fact]
        public void ShadowedRule_IsFlaggedUnreachable()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [
                { ""name"": ""early"", ""match"": { ""primary"": false }, ""profile"": { ""height"": 20 } },
                { ""name"": ""late"",  ""match"": { ""primary"": false }, ""profile"": { ""height"": 22, ""margin"": 4 } } ] }");
            Assert.True(Has(r, FindingSeverity.Warning, "rule 'early' is unreachable"));
            Assert.False(Has(r, FindingSeverity.Warning, "rule 'late' is unreachable"));
        }

        [Fact]
        public void PartiallyOverriddenRule_IsNotUnreachable()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [
                { ""name"": ""early"", ""match"": { ""primary"": false }, ""profile"": { ""height"": 20, ""margin"": 3 } },
                { ""name"": ""late"",  ""match"": { ""primary"": false }, ""profile"": { ""height"": 22 } } ] }");
            Assert.DoesNotContain(r.Findings, f => f.Message.Contains("unreachable"));
        }

        [Fact]
        public void NarrowRuleBeforeBroad_IsNotUnreachable()
        {
            // The narrow rule applies later (more specific), so the broad one cannot shadow it.
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [
                { ""name"": ""narrow"", ""match"": { ""primary"": false, ""minWidth"": 3000 }, ""profile"": { ""height"": 20 } },
                { ""name"": ""broad"",  ""match"": { ""primary"": false }, ""profile"": { ""height"": 22 } } ] }");
            Assert.DoesNotContain(r.Findings, f => f.Message.Contains("unreachable"));
        }

        [Fact]
        public void EmptyRule_WarnsThatItSetsNothing_AndEmptyMatchIsInfo()
        {
            var r = ProfilesConfigParser.Parse(@"{ ""rules"": [ { ""name"": ""x"" } ] }");
            Assert.True(Has(r, FindingSeverity.Warning, "sets nothing"));
            Assert.True(Has(r, FindingSeverity.Info, "matches every monitor"));
        }

        [Fact]
        public void FindingToString_IncludesSeverityPathAndMessage()
        {
            var f = new ConfigFinding(FindingSeverity.Error, "a.b", "boom");
            Assert.Equal("error a.b: boom", f.ToString());
        }

        [Fact]
        public void ParsedConfig_FeedsResolverEndToEnd()
        {
            var cfg = ProfilesConfigParser.Parse(Full).Config;
            var mons = Mon.Set(Mon.Fhd(), Mon.Ultra(), Mon.Portrait());
            var map = ProfileResolver.Resolve(mons, cfg);
            Assert.Equal(true, map[@"\\.\DISPLAY2"].Settings.Floating);
            Assert.Equal(8, map[@"\\.\DISPLAY2"].Settings.Margin);
            Assert.Equal(TaskbarEdge.Left, map[@"\\.\DISPLAY3"].Settings.Position);
            Assert.Equal(32, map[@"\\.\DISPLAY1"].Settings.Height);
            Assert.Equal(false, map[@"\\.\DISPLAY1"].Settings.Floating);
        }
    }
}
