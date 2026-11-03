using System;
using System.Collections.Generic;
using CairoDesktop.Features.CommandPalette;
using Xunit;

namespace CairoDesktop.Features.Tests.CommandPalette
{
    /// <summary>Regression tests from the adversarial review of the command palette.</summary>
    public class ReviewRegressionTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("https://{q}")]
        [InlineData("https://{q}.example.com/")]
        [InlineData("https://example.com@{q}/")]
        [InlineData("https://{q}?x=1")]
        public void WebSearchTemplateMayNotPutQueryInAuthority(string template)
        {
            Assert.NotNull(PaletteConfigParser.ValidateWebSearchUrl(template));
        }

        [Theory]
        [InlineData("https://example.com/search?q={q}")]
        [InlineData("https://example.com/{q}")]
        [InlineData("https://example.com?q={q}&b={q}")]
        [InlineData("https://example.com#{q}")]
        public void WebSearchTemplateAllowsQueryAfterHost(string template)
        {
            Assert.Null(PaletteConfigParser.ValidateWebSearchUrl(template));
        }

        private sealed class BadList : ICommandProvider
        {
            public string Name => "bad";
            public IReadOnlyList<PaletteItem> Query(PaletteQuery q) => new ThrowingList();
        }

        private sealed class ThrowingList : IReadOnlyList<PaletteItem>
        {
            public PaletteItem this[int index] => throw new InvalidOperationException("boom");
            public int Count => 3;
            public IEnumerator<PaletteItem> GetEnumerator() => throw new InvalidOperationException("boom");
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Fact]
        public void ProviderListThatThrowsWhileIndexedIsIsolated()
        {
            var e = new PaletteEngine(new ICommandProvider[] { new BadList(), new StaticItemProvider("ok", new[] { new PaletteItem("1", "Fine") }) });
            var r = e.Search("", 5);
            Assert.Single(r.Results);
            Assert.Single(r.Diagnostics);
        }

        [Fact]
        public void AppsToggleOffHidesHostAppItems()
        {
            var cfg = new PaletteConfig();
            cfg.Providers.Apps = false;
            var host = new StaticItemProvider("apps", new[]
            {
                new PaletteItem("a1", "Firefox", null, PaletteCategories.App),
                new PaletteItem("h1", "Host thing", null, PaletteCategories.Command)
            });
            var r = PaletteEngine.Create(cfg, new[] { host }, null).Search("", 10);
            Assert.DoesNotContain(r.Results, x => x.Item.Id == "a1");
            Assert.Contains(r.Results, x => x.Item.Id == "h1");
        }

        [Fact]
        public void FrecencyWithFutureTimestampStillDecays()
        {
            string json = "{\"entries\":[{\"id\":\"a\",\"score\":8,\"last\":\"9999-01-01T00:00:00+00:00\"}]}";
            DateTimeOffset now = T0;
            var s = FrecencyStore.FromJson(json, () => now, null, 500, TimeSpan.FromDays(10));
            now = T0.AddDays(10);
            Assert.True(s.GetScore("a") < 4.5, "score should have halved after one half-life");
        }

        [Fact]
        public void FrecencyIdWithLoneSurrogateDoesNotBreakSerialisation()
        {
            var s = new FrecencyStore(() => T0);
            s.Record("app:bad\uD800name");
            s.Record("app:ok");
            string json = s.ToJson();
            var back = FrecencyStore.FromJson(json, () => T0);
            Assert.True(back.GetScore("app:ok") > 0);
        }

        [Fact]
        public void FuzzyDoesNotSplitSurrogatePairs()
        {
            string grin = new string(new[] { (char)0xD83D, (char)0xDE01 });   // U+1F601
            string laugh = new string(new[] { (char)0xD83D, (char)0xDE00 });  // U+1F600
            var m = FuzzyMatcher.Match(laugh, "x" + grin + laugh);
            Assert.True(m.IsMatch);
            Assert.Equal(new[] { 3, 4 }, m.Indices);
            // High half of one emoji and low half of another must not combine into a match.
            string mixed = grin + " " + new string(new[] { (char)0xD83C, (char)0xDE00 });
            Assert.False(FuzzyMatcher.Match(laugh, mixed).IsMatch);
        }

        [Theory]
        [InlineData("Meta+Space", "Win+Space")]
        [InlineData("Ctrl+PgUp", "Ctrl+PageUp")]
        [InlineData("Alt+Return", "Alt+Enter")]
        public void HotkeyAcceptsAliasesTheRealParserAccepts(string input, string expected)
        {
            Assert.Equal(expected, PaletteConfigParser.NormalizeHotkey(input, out _));
        }
    }
}
