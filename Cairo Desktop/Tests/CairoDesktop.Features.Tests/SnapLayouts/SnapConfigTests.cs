using System.Linq;
using CairoDesktop.Features.SnapLayouts;
using Xunit;

namespace CairoDesktop.Features.Tests.SnapLayouts
{
    public class SnapConfigTests
    {
        private static SnapParseResult Section(string json) => SnapConfigParser.ParseSection(json);

        [Fact]
        public void DefaultsAreOffAndFindingFree()
        {
            SnapParseResult r = SnapConfigParser.ParseDocument("{ \"theme\": \"x\" }");
            Assert.False(r.Config.Enabled);
            Assert.Empty(r.Findings);
            Assert.Equal(0, r.Config.Gap);
            Assert.True(r.Config.Wrap);
            Assert.Equal(SnapConfig.ActionNames.Length, r.Config.Hotkeys.Count);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyInputIsDefaults(string json)
        {
            Assert.Empty(SnapConfigParser.ParseDocument(json).Findings);
            Assert.Empty(SnapConfigParser.ParseSection(json).Findings);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[1,2")]
        [InlineData("\"str\"")]
        [InlineData("42")]
        [InlineData("null")]
        public void GarbageNeverThrows(string json)
        {
            SnapParseResult a = SnapConfigParser.ParseDocument(json);
            SnapParseResult b = SnapConfigParser.ParseSection(json);
            Assert.NotNull(a.Config);
            Assert.NotNull(b.Config);
            Assert.True(a.HasErrors || json == "null" || a.Findings.Count == 0);
        }

        [Fact]
        public void FullSectionParses()
        {
            const string json = @"{
              // comments and trailing commas are fine
              ""snapLayouts"": {
                ""enabled"": true, ""gap"": 8, ""outerGap"": 12, ""defaultLayout"": ""thirds"",
                ""wrap"": false, ""snapMaximized"": true,
                ""layouts"": [ { ""id"": ""sidebar"", ""name"": ""Sidebar"", ""zones"": [
                    { ""x"": 0, ""y"": 0, ""w"": 0.2, ""h"": 1 }, { ""x"": 0.2, ""y"": 0, ""w"": 0.8, ""h"": 1 }, ] } ],
                ""hotkeys"": { ""snapLeft"": ""Ctrl+Alt+Left"", ""restore"": """" },
                ""monitors"": { ""primary"": ""sidebar"", ""3440x1440"": ""ultrawide"" },
              }
            }";
            SnapParseResult r = SnapConfigParser.ParseDocument(json);
            Assert.Empty(r.Findings);
            SnapConfig c = r.Config;
            Assert.True(c.Enabled);
            Assert.Equal(8, c.Gap);
            Assert.Equal(12, c.OuterGap);
            Assert.Equal("thirds", c.DefaultLayout);
            Assert.False(c.Wrap);
            Assert.True(c.SnapMaximized);
            Assert.Single(c.CustomLayouts);
            Assert.Equal(2, c.CustomLayouts[0].Zones.Count);
            Assert.Equal("Ctrl+Alt+Left", c.Hotkeys["snapLeft"]);
            Assert.Equal("", c.Hotkeys["restore"]);
            Assert.Equal("Win+Alt+Right", c.Hotkeys["snapRight"]);
            Assert.Equal("sidebar", c.MonitorLayouts["primary"]);
            Assert.NotNull(c.BuildCatalog().Find("sidebar"));
        }

        [Fact]
        public void PropertyNamesAreCaseInsensitive()
        {
            SnapParseResult r = Section(@"{ ""ENABLED"": true, ""OuterGap"": 4 }");
            Assert.True(r.Config.Enabled);
            Assert.Equal(4, r.Config.OuterGap);
        }

        [Fact]
        public void SectionMustBeAnObject()
        {
            SnapParseResult r = SnapConfigParser.ParseDocument(@"{ ""snapLayouts"": [] }");
            Assert.Contains(r.Findings, f => f.Severity == FindingSeverity.Error && f.Path == "snapLayouts");
        }

        [Fact]
        public void WrongTypeOfEnabledWarns()
        {
            SnapParseResult r = Section(@"{ ""enabled"": ""yes"" }");
            Assert.False(r.Config.Enabled);
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.enabled" && f.Severity == FindingSeverity.Warning);
        }

        [Theory]
        [InlineData("-5", 0)]
        [InlineData("5000", SnapConfigParser.MaxGap)]
        [InlineData("7.6", 8)]
        public void GapIsClamped(string value, int expected)
        {
            SnapParseResult r = Section("{ \"gap\": " + value + " }");
            Assert.Equal(expected, r.Config.Gap);
        }

        [Fact]
        public void NonNumericGapWarnsAndUsesZero()
        {
            SnapParseResult r = Section(@"{ ""gap"": ""big"" }");
            Assert.Equal(0, r.Config.Gap);
            Assert.Single(r.Findings);
        }

        [Fact]
        public void UnknownSettingIsAWarning()
        {
            SnapParseResult r = Section(@"{ ""bogus"": 1 }");
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.bogus" && f.Severity == FindingSeverity.Warning);
            Assert.False(r.HasErrors);
        }

        [Fact]
        public void UnknownDefaultLayoutIsDroppedWithWarning()
        {
            SnapParseResult r = Section(@"{ ""defaultLayout"": ""ghost"" }");
            Assert.Null(r.Config.DefaultLayout);
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.defaultLayout");
        }

        [Fact]
        public void DefaultLayoutMayBeACustomOne()
        {
            SnapParseResult r = Section(@"{ ""defaultLayout"": ""mine"", ""layouts"": [ { ""id"": ""mine"", ""zones"": [ { ""x"":0,""y"":0,""w"":1,""h"":1 } ] } ] }");
            Assert.Equal("mine", r.Config.DefaultLayout);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void CustomLayoutWithOverlapIsRejectedUnlessAllowed()
        {
            const string zones = @"""zones"": [ { ""x"":0,""y"":0,""w"":0.6,""h"":1 }, { ""x"":0.5,""y"":0,""w"":0.5,""h"":1 } ]";
            SnapParseResult bad = Section(@"{ ""layouts"": [ { ""id"": ""o"", " + zones + " } ] }");
            Assert.Empty(bad.Config.CustomLayouts);
            Assert.Contains(bad.Findings, f => f.Severity == FindingSeverity.Error && f.Message.Contains("overlap"));

            SnapParseResult ok = Section(@"{ ""layouts"": [ { ""id"": ""o"", ""allowOverlap"": true, " + zones + " } ] }");
            Assert.Single(ok.Config.CustomLayouts);
            Assert.Empty(ok.Findings);
        }

        [Fact]
        public void CustomLayoutProblemsAreReportedPerLayout()
        {
            const string json = @"{ ""layouts"": [
                { ""id"": ""good"", ""zones"": [ { ""x"":0,""y"":0,""w"":1,""h"":1 } ] },
                { ""id"": ""outside"", ""zones"": [ { ""x"":0.5,""y"":0,""w"":1,""h"":1 } ] },
                { ""id"": ""zero"", ""zones"": [ { ""x"":0,""y"":0,""w"":0,""h"":1 } ] },
                { ""zones"": [ { ""x"":0,""y"":0,""w"":1,""h"":1 } ] },
                { ""id"": ""missing"", ""zones"": [ { ""x"":0,""y"":0,""w"":1 } ] },
                { ""id"": ""nozones"" },
                { ""id"": ""notarray"", ""zones"": 5 },
                7
              ] }";
            SnapParseResult r = Section(json);
            Assert.Equal(new[] { "good" }, r.Config.CustomLayouts.Select(l => l.Id).ToArray());
            Assert.True(r.Findings.Count(f => f.Severity == FindingSeverity.Error) >= 7);
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.layouts[1]");
            Assert.Contains(r.Findings, f => f.Path.StartsWith("snapLayouts.layouts[4]"));
        }

        [Fact]
        public void DuplicateAndBuiltInIdsAreRejected()
        {
            const string z = @"""zones"": [ { ""x"":0,""y"":0,""w"":1,""h"":1 } ]";
            SnapParseResult r = Section(@"{ ""layouts"": [ { ""id"": ""a"", " + z + @" }, { ""id"": ""A"", " + z + @" }, { ""id"": ""thirds"", " + z + " } ] }");
            Assert.Single(r.Config.CustomLayouts);
            Assert.Equal(2, r.Findings.Count(f => f.Message.Contains("already used")));
        }

        [Fact]
        public void ZoneWidthAndHeightLongNamesAreAccepted()
        {
            SnapParseResult r = Section(@"{ ""layouts"": [ { ""id"": ""a"", ""zones"": [ { ""x"":0,""y"":0,""width"":1,""height"":1 } ] } ] }");
            Assert.Single(r.Config.CustomLayouts);
        }

        [Fact]
        public void UnknownHotkeyActionWarns()
        {
            SnapParseResult r = Section(@"{ ""hotkeys"": { ""explode"": ""Ctrl+Alt+X"" } }");
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.hotkeys.explode" && f.Severity == FindingSeverity.Warning);
        }

        [Theory]
        [InlineData("Left")]            // bare key
        [InlineData("Ctrl+")]           // no key
        [InlineData("Ctrl+Alt")]        // only modifiers
        [InlineData("Ctrl+A+B")]        // two keys
        [InlineData("Ctrl++A")]         // empty segment
        public void BadHotkeysKeepTheDefault(string keys)
        {
            SnapParseResult r = Section("{ \"hotkeys\": { \"snapLeft\": \"" + keys + "\" } }");
            Assert.Equal("Win+Alt+Left", r.Config.Hotkeys["snapLeft"]);
            Assert.Contains(r.Findings, f => f.Severity == FindingSeverity.Error && f.Path == "snapLayouts.hotkeys.snapLeft");
        }

        [Theory]
        [InlineData("F5")]
        [InlineData("ctrl + shift + F12")]
        [InlineData("Win+Alt+Left")]
        [InlineData("Ctrl++")]
        [InlineData("Super+Meta+X")]
        public void GoodHotkeysAreAccepted(string keys)
        {
            Assert.Null(HotkeySyntax.Check(keys));
        }

        [Fact]
        public void ActionNamesAreCaseInsensitiveAndNormalised()
        {
            SnapParseResult r = Section(@"{ ""hotkeys"": { ""SNAPLEFT"": ""Ctrl+Alt+J"" } }");
            Assert.Equal("Ctrl+Alt+J", r.Config.Hotkeys["snapLeft"]);
            Assert.Empty(r.Findings);
        }

        [Fact]
        public void NullDisablesAnAction()
        {
            SnapParseResult r = Section(@"{ ""hotkeys"": { ""cycleLayout"": null } }");
            Assert.Equal("", r.Config.Hotkeys["cycleLayout"]);
        }

        [Fact]
        public void DuplicateHotkeysDisableTheLaterAction()
        {
            SnapParseResult r = Section(@"{ ""hotkeys"": { ""snapRight"": ""alt+WIN+left"" } }");
            Assert.Equal("Win+Alt+Left", r.Config.Hotkeys["snapLeft"]);
            Assert.Equal("", r.Config.Hotkeys["snapRight"]);
            Assert.Contains(r.Findings, f => f.Path == "snapLayouts.hotkeys.snapRight" && f.Message.Contains("already used"));
        }

        [Fact]
        public void HotkeyCanonicalIgnoresOrderCaseAndAliases()
        {
            Assert.Equal(HotkeySyntax.Canonical("Win+Alt+Left"), HotkeySyntax.Canonical("alt + windows + LEFT"));
            Assert.NotEqual(HotkeySyntax.Canonical("Win+Alt+Left"), HotkeySyntax.Canonical("Win+Alt+Right"));
        }

        [Fact]
        public void MonitorEntryWithUnknownLayoutIsDropped()
        {
            SnapParseResult r = Section(@"{ ""monitors"": { ""primary"": ""ghost"", ""1920x1080"": ""thirds"", ""x"": 5 } }");
            Assert.False(r.Config.MonitorLayouts.ContainsKey("primary"));
            Assert.Equal("thirds", r.Config.MonitorLayouts["1920x1080"]);
            Assert.Equal(2, r.Findings.Count);
        }

        [Fact]
        public void ParsedConfigDrivesThePickerEndToEnd()
        {
            SnapParseResult r = Section(@"{ ""enabled"": true, ""monitors"": { ""primary"": ""quarters"" } }");
            SnapLayout l = LayoutPicker.Pick(r.Config.BuildCatalog(), new MonitorDescriptor("d", 1920, 1080, true), r.Config.MonitorLayouts, r.Config.DefaultLayout);
            Assert.Equal("quarters", l.Id);
        }

        [Fact]
        public void FindingToStringIsReadable()
        {
            Assert.Equal("warning: a.b: msg", new SnapFinding(FindingSeverity.Warning, "a.b", "msg").ToString());
        }
    }
}
