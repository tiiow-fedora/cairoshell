using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.CommandPalette;
using Xunit;

namespace CairoDesktop.Features.Tests.CommandPalette
{
    public class QueryTests
    {
        [Theory]
        [InlineData("", PaletteMode.All, "")]
        [InlineData(null, PaletteMode.All, "")]
        [InlineData("calc", PaletteMode.All, "calc")]
        [InlineData(">  reload ", PaletteMode.Commands, "reload")]
        [InlineData("  >reload", PaletteMode.Commands, "reload")]
        [InlineData("?cats & dogs", PaletteMode.Web, "cats & dogs")]
        [InlineData("=1+1", PaletteMode.Calculator, "1+1")]
        [InlineData("@fire", PaletteMode.Apps, "fire")]
        [InlineData(">", PaletteMode.Commands, "")]
        [InlineData(">>x", PaletteMode.Commands, ">x")]
        public void ParsesPrefix(string raw, PaletteMode mode, string text)
        {
            var q = PaletteQuery.Parse(raw);
            Assert.Equal(mode, q.Mode);
            Assert.Equal(text, q.Text);
        }

        [Fact]
        public void PrefixInMiddleIsNotAMode() => Assert.Equal(PaletteMode.All, PaletteQuery.Parse("a>b").Mode);

        [Fact]
        public void LongTextIsCut() => Assert.Equal(PaletteQuery.MaxTextLength, PaletteQuery.Parse(new string('x', 100000)).Text.Length);

        [Fact]
        public void RawIsKept() => Assert.Equal(" >x ", PaletteQuery.Parse(" >x ").Raw);
    }

    public class PaletteActionTests
    {
        [Fact]
        public void PayloadRoundTrips()
        {
            Assert.True(PaletteAction.TryParse(PaletteAction.Launch("C:\\x.exe", "-a b").ToPayload(), out var a));
            Assert.Equal(PaletteActionKind.Launch, a.Kind);
            Assert.Equal("C:\\x.exe", a.Target);
            Assert.Equal("-a b", a.Arguments);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("launch")]
        [InlineData("bogus\u001fx\u001f")]
        [InlineData("launch\u001f\u001f")]
        [InlineData("99\u001fx\u001f")]
        public void BadPayloadsDoNotParse(string payload) => Assert.False(PaletteAction.TryParse(payload, out _));

        [Fact]
        public void SeparatorIsStrippedFromValues() =>
            Assert.True(PaletteAction.TryParse(PaletteAction.Launch("a\u001fb", "c\u001fd").ToPayload(), out var a) && a.Target == "ab" && a.Arguments == "cd");

        [Theory]
        [InlineData("http://example.com", true)]
        [InlineData("https://example.com/a?b=c%20d", true)]
        [InlineData("HTTPS://EXAMPLE.COM", true)]
        [InlineData("file:///c:/windows/system32/calc.exe", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("ftp://example.com", false)]
        [InlineData("calc.exe", false)]
        [InlineData("https://", false)]
        [InlineData("https://exa mple.com", false)]
        [InlineData("https://example.com/\r\nX: y", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void SafeWebUrl(string url, bool expected) => Assert.Equal(expected, PaletteAction.IsSafeWebUrl(url));
    }

    public class EngineTests
    {
        private static PaletteItem Item(string id, string title, string cat = PaletteCategories.Command, string sub = null, string[] kw = null) =>
            new PaletteItem(id, title, sub, cat, kw, "launch\u001f" + id + "\u001f");

        private sealed class Throwing : ICommandProvider
        {
            public string Name => "Boom";
            public IReadOnlyList<PaletteItem> Query(PaletteQuery q) => throw new InvalidOperationException("kaboom");
        }

        private sealed class ThrowingName : ICommandProvider
        {
            public string Name => throw new Exception("no name");
            public IReadOnlyList<PaletteItem> Query(PaletteQuery q) => null;
        }

        private static PaletteEngine Engine(FrecencyStore f, params PaletteItem[] items) =>
            new PaletteEngine(new[] { new StaticItemProvider("test", items) }, f);

        private static string[] Ids(PaletteSearchResult r) => r.Results.Select(x => x.Item.Id).ToArray();

        [Fact]
        public void EmptyQueryListsAllAlphabetically()
        {
            var r = Engine(null, Item("2", "Beta"), Item("1", "alpha"), Item("3", "Gamma")).Search("", 10);
            Assert.Equal(new[] { "1", "2", "3" }, Ids(r));
        }

        [Fact]
        public void NoMatchesGivesEmptyList() => Assert.Empty(Engine(null, Item("1", "Alpha")).Search("zzz", 10).Results);

        [Fact]
        public void FiltersByFuzzyMatch()
        {
            var r = Engine(null, Item("1", "Open Settings"), Item("2", "Lock"), Item("3", "Sleep")).Search("ops", 10);
            Assert.Equal(new[] { "1" }, Ids(r));
        }

        [Fact]
        public void BetterMatchRanksFirst()
        {
            var r = Engine(null, Item("1", "Reload Config"), Item("2", "Rename Layout Container"), Item("3", "Restart")).Search("rc", 10);
            Assert.Equal("1", Ids(r)[0]);
        }

        [Fact]
        public void TitleMatchGivesHighlightIndices()
        {
            var r = Engine(null, Item("1", "Lock")).Search("lk", 10);
            Assert.Equal(new[] { 0, 3 }, r.Results[0].TitleMatchIndices);
        }

        [Fact]
        public void KeywordMatchFindsItemWithoutHighlight()
        {
            var r = Engine(null, Item("1", "Lock Screen", kw: new[] { "secure", "away" })).Search("secure", 10);
            Assert.Single(r.Results);
            Assert.Empty(r.Results[0].TitleMatchIndices);
        }

        [Fact]
        public void SubtitleMatchFindsItem()
        {
            var r = Engine(null, Item("1", "Foo", sub: "Starts the bar service")).Search("service", 10);
            Assert.Single(r.Results);
        }

        [Fact]
        public void TitleBeatsKeywordOnSameText()
        {
            var r = Engine(null, Item("k", "Zzz", kw: new[] { "settings" }), Item("t", "Settings")).Search("settings", 10);
            Assert.Equal("t", Ids(r)[0]);
        }

        [Fact]
        public void LimitIsApplied()
        {
            var items = Enumerable.Range(0, 30).Select(i => Item("i" + i, "Item " + i)).ToArray();
            Assert.Equal(5, Engine(null, items).Search("", 5).Results.Count);
        }

        [Fact]
        public void ZeroOrNegativeLimitGivesNothing()
        {
            var e = Engine(null, Item("1", "A"));
            Assert.Empty(e.Search("", 0).Results);
            Assert.Empty(e.Search("", -3).Results);
        }

        [Fact]
        public void TiesBreakByTitleThenId()
        {
            var r = Engine(null, Item("b", "Same"), Item("a", "Same"), Item("c", "Alpha Same".Substring(6))).Search("", 10);
            Assert.Equal(new[] { "a", "b", "c" }, Ids(r));
        }

        [Fact]
        public void ResultOrderIsIndependentOfProviderItemOrder()
        {
            var a = Item("1", "Foo"); var b = Item("2", "Foo bar"); var c = Item("3", "Fo");
            var r1 = Engine(null, a, b, c).Search("fo", 10);
            var r2 = Engine(null, c, b, a).Search("fo", 10);
            Assert.Equal(Ids(r1), Ids(r2));
        }

        [Fact]
        public void DuplicateIdsAcrossProvidersKeepFirst()
        {
            var e = new PaletteEngine(new ICommandProvider[]
            {
                new StaticItemProvider("one", new[] { Item("x", "From One") }),
                new StaticItemProvider("two", new[] { Item("x", "From Two") })
            });
            var r = e.Search("", 10);
            Assert.Single(r.Results);
            Assert.Equal("one", r.Results[0].Provider);
        }

        [Fact]
        public void ThrowingProviderBecomesDiagnosticAndOthersStillWork()
        {
            var e = new PaletteEngine(new ICommandProvider[] { new Throwing(), new StaticItemProvider("ok", new[] { Item("1", "Fine") }) });
            var r = e.Search("fine", 10);
            Assert.Single(r.Results);
            Assert.Single(r.Diagnostics);
            Assert.Contains("Boom", r.Diagnostics[0]);
            Assert.Contains("kaboom", r.Diagnostics[0]);
        }

        [Fact]
        public void ProviderWithThrowingNameDoesNotCrash()
        {
            var r = new PaletteEngine(new ICommandProvider[] { new ThrowingName() }).Search("x", 5);
            Assert.Empty(r.Results);
        }

        [Fact]
        public void NullProvidersAndNullItemsAreIgnored()
        {
            var e = new PaletteEngine(new ICommandProvider[] { null, new StaticItemProvider("n", new PaletteItem[] { null, Item("1", "Ok") }) });
            Assert.Single(e.Search("", 5).Results);
        }

        [Fact]
        public void NullProviderListIsFine() => Assert.Empty(new PaletteEngine(null).Search("x", 5).Results);

        [Fact]
        public void ModeCommandsOnlyKeepsCommands()
        {
            var e = Engine(null, Item("c", "Reload", PaletteCategories.Command), Item("a", "Reload App", PaletteCategories.App));
            Assert.Equal(new[] { "c" }, Ids(e.Search(">reload", 10)));
        }

        [Fact]
        public void ModeAppsOnlyKeepsApps()
        {
            var e = Engine(null, Item("c", "Firefox Cmd", PaletteCategories.Command), Item("a", "Firefox", PaletteCategories.App));
            Assert.Equal(new[] { "a" }, Ids(e.Search("@fire", 10)));
        }

        [Fact]
        public void ModeAllMixesCategories()
        {
            var e = Engine(null, Item("c", "Fire Cmd", PaletteCategories.Command), Item("a", "Fire", PaletteCategories.App));
            Assert.Equal(2, e.Search("fire", 10).Results.Count);
        }

        [Fact]
        public void FrecencyBreaksCloseMatches()
        {
            var clock = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var f = new FrecencyStore(() => clock);
            var e = Engine(f, Item("a", "Notes A"), Item("b", "Notes B"));
            Assert.Equal("a", Ids(e.Search("notes", 5))[0]);
            e.RecordLaunch(Item("b", "Notes B"));
            Assert.Equal("b", Ids(e.Search("notes", 5))[0]);
        }

        [Fact]
        public void FrecencyDoesNotOverrideMuchBetterMatch()
        {
            var clock = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var f = new FrecencyStore(() => clock);
            for (int i = 0; i < 100; i++) f.Record("weak");
            var e = Engine(f, Item("weak", "Xs xxxxx xxxe xxxxxx xt"), Item("strong", "Settings"));
            Assert.Equal("strong", Ids(e.Search("set", 5))[0]);
        }

        [Fact]
        public void FrecencyOrdersEmptyQuery()
        {
            var clock = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var f = new FrecencyStore(() => clock);
            f.Record("z");
            var e = Engine(f, Item("a", "Alpha"), Item("z", "Zulu"));
            Assert.Equal(new[] { "z", "a" }, Ids(e.Search("", 5)));
        }

        [Fact]
        public void RecordLaunchIgnoresNullPinnedAndMissingStore()
        {
            var clock = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var f = new FrecencyStore(() => clock);
            new PaletteEngine(null, f).RecordLaunch(null);
            new PaletteEngine(null, f).RecordLaunch(new PaletteItem("p", "P", pinnedScore: 5));
            new PaletteEngine(null, null).RecordLaunch(Item("x", "X"));
            Assert.Equal(0, f.Count);
        }

        [Fact]
        public void PinnedItemsSkipMatchingAndRankByPin()
        {
            var e = Engine(null, new PaletteItem("p", "Totally unrelated", pinnedScore: 10000), Item("1", "Alpha"));
            Assert.Equal("p", Ids(e.Search("alp", 5))[0]);
        }

        [Fact]
        public void HugeProviderOutputIsCappedWithDiagnostic()
        {
            var items = Enumerable.Range(0, 6000).Select(i => Item("i" + i, "x")).ToArray();
            var r = Engine(null, items).Search("", 10000);
            Assert.Equal(5000, r.Results.Count);
            Assert.Single(r.Diagnostics);
        }

        [Fact]
        public void VeryLongQueryDoesNotThrow()
        {
            var r = Engine(null, Item("1", "Alpha")).Search(new string('a', 100000), 5);
            Assert.NotNull(r);
        }

        [Fact]
        public void UnicodeQueryMatches()
        {
            Assert.Single(Engine(null, Item("1", "Überprüfung")).Search("üb", 5).Results);
        }

        [Fact]
        public void SearchIsDeterministicAcrossRuns()
        {
            var items = Enumerable.Range(0, 50).Select(i => Item("i" + i, "Thing " + (i % 7))).ToArray();
            var e = Engine(null, items);
            Assert.Equal(Ids(e.Search("thing", 20)), Ids(e.Search("thing", 20)));
        }
    }

    public class ProviderTests
    {
        [Fact]
        public void CalculatorModeReturnsPinnedCopyResult()
        {
            var items = new CalculatorProvider().Query(PaletteQuery.Parse("=2+3*4"));
            Assert.Single(items);
            Assert.Equal("14", items[0].Title);
            Assert.True(items[0].PinnedScore.HasValue);
            Assert.True(PaletteAction.TryParse(items[0].Payload, out var a));
            Assert.Equal(PaletteActionKind.Copy, a.Kind);
            Assert.Equal("14", a.Target);
        }

        [Fact]
        public void CalculatorModeShowsErrorItemWithoutPayload()
        {
            var items = new CalculatorProvider().Query(PaletteQuery.Parse("=1/0"));
            Assert.Single(items);
            Assert.Equal("", items[0].Payload);
        }

        [Fact]
        public void CalculatorAnswersInAllModeForMathLookingText()
        {
            Assert.Single(new CalculatorProvider().Query(PaletteQuery.Parse("12*12")));
        }

        [Theory]
        [InlineData("notepad")]
        [InlineData("7-zip")]
        [InlineData("2024")]
        [InlineData("1/0")]
        [InlineData("")]
        public void CalculatorStaysQuietForOrdinaryText(string text) => Assert.Empty(new CalculatorProvider().Query(PaletteQuery.Parse(text)));

        [Fact]
        public void CalculatorIgnoresOtherModes() => Assert.Empty(new CalculatorProvider().Query(PaletteQuery.Parse(">1+1")));

        [Fact]
        public void CalculatorEmptyEqualsGivesNothing() => Assert.Empty(new CalculatorProvider().Query(PaletteQuery.Parse("=")));

        [Fact]
        public void WebSearchEncodesQuery()
        {
            var items = new WebSearchProvider("https://example.com/s?q={q}").Query(PaletteQuery.Parse("?cats & dogs/é"));
            Assert.Single(items);
            PaletteAction.TryParse(items[0].Payload, out var a);
            Assert.Equal("https://example.com/s?q=cats%20%26%20dogs%2F%C3%A9", a.Target);
            Assert.True(PaletteAction.IsSafeWebUrl(a.Target));
        }

        [Fact]
        public void WebSearchOnlyInWebMode()
        {
            var p = new WebSearchProvider("https://example.com/?q={q}");
            Assert.Empty(p.Query(PaletteQuery.Parse("cats")));
            Assert.Empty(p.Query(PaletteQuery.Parse("?")));
        }

        [Fact]
        public void WebSearchWithBadTemplateFallsBackToDefault()
        {
            var items = new WebSearchProvider("javascript:alert({q})").Query(PaletteQuery.Parse("?x"));
            PaletteAction.TryParse(items[0].Payload, out var a);
            Assert.StartsWith("https://", a.Target);
        }

        [Fact]
        public void WebSearchHandlesLongInputUnderUriLimit()
        {
            var items = new WebSearchProvider("https://example.com/?q={q}").Query(PaletteQuery.Parse("?" + new string('é', 50000)));
            Assert.Single(items);
        }

        [Fact]
        public void UserCommandsBecomeLaunchItems()
        {
            var p = new UserCommandProvider(new[] { new UserCommand("Open Notes", "notepad.exe", "notes.txt"), new UserCommand("", "x", ""), null });
            var items = p.Query(PaletteQuery.Parse(""));
            Assert.Single(items);
            Assert.Equal(PaletteCategories.Command, items[0].Category);
            PaletteAction.TryParse(items[0].Payload, out var a);
            Assert.Equal("notepad.exe", a.Target);
            Assert.Equal("notes.txt", a.Arguments);
        }

        [Fact]
        public void UserCommandsWithSameTitleGetDistinctIds()
        {
            var items = new UserCommandProvider(new[] { new UserCommand("A", "x", ""), new UserCommand("A", "y", "") }).Query(PaletteQuery.Parse(""));
            Assert.NotEqual(items[0].Id, items[1].Id);
        }

        [Fact]
        public void CreateRespectsToggles()
        {
            var cfg = new PaletteConfig();
            cfg.Providers.Calculator = false;
            cfg.Providers.WebSearch = false;
            cfg.Commands.Add(new UserCommand("Hello", "x", ""));
            var e = PaletteEngine.Create(cfg, null, null);
            Assert.Empty(e.Search("=1+1", 5).Results);
            Assert.Empty(e.Search("?cats", 5).Results);
            Assert.Single(e.Search("hello", 5).Results);
        }

        [Fact]
        public void CreateWithDefaultsAnswersEverything()
        {
            var e = PaletteEngine.Create(null, null, null);
            Assert.Single(e.Search("=1+1", 5).Results);
            Assert.Single(e.Search("?cats", 5).Results);
        }
    }

    public class ConfigTests
    {
        [Fact]
        public void EmptyInputGivesDefaults()
        {
            var r = PaletteConfigParser.Parse("");
            Assert.Empty(r.Findings);
            Assert.True(r.Config.Enabled);
            Assert.Equal(PaletteConfig.DefaultHotkey, r.Config.Hotkey);
            Assert.Equal(PaletteConfig.DefaultMaxResults, r.Config.MaxResults);
        }

        [Fact]
        public void ParsesFullSectionWithCommentsAndTrailingCommas()
        {
            string json = @"{
              // palette
              ""enabled"": true,
              ""hotkey"": ""ctrl + shift + space"",
              ""maxResults"": 12,
              ""webSearchUrl"": ""https://example.com/search?q={q}"",
              ""providers"": { ""apps"": false, ""CALCULATOR"": true, },
              ""commands"": [ { ""title"": ""Notes"", ""run"": ""notepad.exe"", ""args"": ""a.txt"" }, ],
            }";
            var r = PaletteConfigParser.Parse(json);
            Assert.Empty(r.Findings);
            Assert.Equal("Ctrl+Shift+Space", r.Config.Hotkey);
            Assert.Equal(12, r.Config.MaxResults);
            Assert.Equal("https://example.com/search?q={q}", r.Config.WebSearchUrl);
            Assert.False(r.Config.Providers.Apps);
            Assert.True(r.Config.Providers.Calculator);
            Assert.Single(r.Config.Commands);
            Assert.Equal("a.txt", r.Config.Commands[0].Args);
        }

        [Fact]
        public void PropertyNamesAreCaseInsensitive()
        {
            Assert.Equal(7, PaletteConfigParser.Parse("{\"MAXRESULTS\": 7}").Config.MaxResults);
        }

        [Fact]
        public void ParseFromRootFindsSection()
        {
            var r = PaletteConfigParser.ParseFromRoot("{ \"enabled\": false, \"commandPalette\": { \"maxResults\": 3 } }");
            Assert.Equal(3, r.Config.MaxResults);
        }

        [Fact]
        public void ParseFromRootWithoutSectionIsDefaultsNoFindings()
        {
            var r = PaletteConfigParser.ParseFromRoot("{ \"enabled\": true }");
            Assert.Empty(r.Findings);
            Assert.Equal(PaletteConfig.DefaultMaxResults, r.Config.MaxResults);
        }

        [Fact]
        public void InvalidJsonGivesFindingAndDefaults()
        {
            var r = PaletteConfigParser.Parse("{ nope");
            Assert.Single(r.Findings);
            Assert.Equal(PaletteConfig.DefaultHotkey, r.Config.Hotkey);
            Assert.Single(PaletteConfigParser.ParseFromRoot("[[[").Findings.Take(1));
        }

        [Fact]
        public void NonObjectSectionIsFinding() => Assert.Single(PaletteConfigParser.Parse("[1]").Findings);

        [Theory]
        [InlineData("ftp://example.com/?q={q}")]
        [InlineData("https://example.com/")]
        [InlineData("javascript:alert('{q}')")]
        [InlineData("file:///c:/{q}")]
        [InlineData("not a url {q}")]
        [InlineData("")]
        [InlineData("{q}")]
        public void BadWebSearchUrlIsRejectedWithFinding(string url)
        {
            var r = PaletteConfigParser.Parse("{\"webSearchUrl\": " + System.Text.Json.JsonSerializer.Serialize(url) + "}");
            Assert.Single(r.Findings);
            Assert.Equal(PaletteConfig.DefaultWebSearchUrl, r.Config.WebSearchUrl);
        }

        [Theory]
        [InlineData("http://example.com/?q={q}")]
        [InlineData("https://example.com/{q}/results")]
        public void GoodWebSearchUrlIsAccepted(string url) => Assert.Null(PaletteConfigParser.ValidateWebSearchUrl(url));

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(1000, 50)]
        public void MaxResultsIsClampedWithFinding(int given, int expected)
        {
            var r = PaletteConfigParser.Parse("{\"maxResults\": " + given + "}");
            Assert.Equal(expected, r.Config.MaxResults);
            Assert.Single(r.Findings);
        }

        [Fact]
        public void WrongTypesAreFindings()
        {
            var r = PaletteConfigParser.Parse("{\"maxResults\": \"ten\", \"enabled\": \"yes\", \"hotkey\": 5, \"webSearchUrl\": 3, \"providers\": 1, \"commands\": {}}");
            Assert.Equal(6, r.Findings.Count);
        }

        [Fact]
        public void UnknownSettingAndProviderAreFindings()
        {
            var r = PaletteConfigParser.Parse("{\"colour\": 1, \"providers\": {\"files\": true, \"apps\": \"no\"}}");
            Assert.Equal(3, r.Findings.Count);
        }

        [Fact]
        public void BadUserCommandsAreSkippedWithFindings()
        {
            var r = PaletteConfigParser.Parse("{\"commands\": [ {\"title\":\"Ok\",\"run\":\"x\"}, {\"title\":\"NoRun\"}, {\"run\":\"y\"}, 5, {\"title\":\" \",\"run\":\"z\"} ]}");
            Assert.Single(r.Config.Commands);
            Assert.Equal(4, r.Findings.Count);
        }

        [Fact]
        public void UserCommandsAreCapped()
        {
            var cmds = string.Join(",", Enumerable.Range(0, 250).Select(i => "{\"title\":\"c" + i + "\",\"run\":\"x\"}"));
            var r = PaletteConfigParser.Parse("{\"commands\":[" + cmds + "]}");
            Assert.Equal(200, r.Config.Commands.Count);
            Assert.Single(r.Findings);
        }

        [Fact]
        public void NullSectionIsDefaults() => Assert.Empty(PaletteConfigParser.Parse("null").Findings);

        [Theory]
        [InlineData("Alt+Space", "Alt+Space")]
        [InlineData("control+alt+k", "Ctrl+Alt+K")]
        [InlineData("Win+Shift+F12", "Shift+Win+F12")]
        [InlineData("super + 1", "Win+1")]
        [InlineData("Ctrl+esc", "Ctrl+Escape")]
        [InlineData("alt+pagedown", "Alt+PageDown")]
        public void HotkeyNormalisation(string given, string expected) => Assert.Equal(expected, PaletteConfigParser.NormalizeHotkey(given, out _));

        [Theory]
        [InlineData("")]
        [InlineData("Space")]
        [InlineData("Ctrl")]
        [InlineData("Ctrl+")]
        [InlineData("Ctrl+A+B")]
        [InlineData("Ctrl+F25")]
        [InlineData("Ctrl+F0")]
        [InlineData("Ctrl+Banana")]
        [InlineData("Ctrl+;")]
        public void BadHotkeys(string given)
        {
            Assert.Null(PaletteConfigParser.NormalizeHotkey(given, out string error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void BadHotkeyFallsBackToDefaultWithFinding()
        {
            var r = PaletteConfigParser.Parse("{\"hotkey\": \"Banana\"}");
            Assert.Equal(PaletteConfig.DefaultHotkey, r.Config.Hotkey);
            Assert.Single(r.Findings);
        }

        [Fact]
        public void ParsingIsCultureIndependent()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                Assert.Equal("Ctrl+F1", PaletteConfigParser.NormalizeHotkey("CTRL+f1", out _));
                Assert.Equal("Ctrl+I", PaletteConfigParser.NormalizeHotkey("ctrl+i", out _));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }
    }
}
