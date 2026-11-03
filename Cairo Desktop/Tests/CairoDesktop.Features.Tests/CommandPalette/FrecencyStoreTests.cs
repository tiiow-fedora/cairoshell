using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CairoDesktop.Features.CommandPalette;
using Xunit;

namespace CairoDesktop.Features.Tests.CommandPalette
{
    public class FrecencyStoreTests
    {
        private sealed class Clock
        {
            public DateTimeOffset Now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public DateTimeOffset Get() => Now;
        }

        private static (FrecencyStore, Clock) Make(int max = 500, TimeSpan? halfLife = null)
        {
            var c = new Clock();
            return (new FrecencyStore(c.Get, max, halfLife ?? TimeSpan.FromDays(10)), c);
        }

        [Fact]
        public void UnknownIdScoresZero()
        {
            var (s, _) = Make();
            Assert.Equal(0, s.GetScore("x"));
            Assert.Equal(0, s.GetCount("x"));
        }

        [Fact]
        public void RecordIncreasesScoreAndCount()
        {
            var (s, _) = Make();
            s.Record("a");
            s.Record("a");
            Assert.Equal(2, s.GetScore("a"), 9);
            Assert.Equal(2, s.GetCount("a"));
        }

        [Fact]
        public void ScoreHalvesAfterHalfLife()
        {
            var (s, c) = Make();
            s.Record("a");
            c.Now += TimeSpan.FromDays(10);
            Assert.Equal(0.5, s.GetScore("a"), 9);
            c.Now += TimeSpan.FromDays(10);
            Assert.Equal(0.25, s.GetScore("a"), 9);
        }

        [Fact]
        public void RecentLaunchOutweighsOldLaunches()
        {
            var (s, c) = Make();
            for (int i = 0; i < 3; i++) s.Record("old");
            c.Now += TimeSpan.FromDays(60);
            s.Record("new");
            Assert.True(s.GetScore("new") > s.GetScore("old"));
        }

        [Fact]
        public void ClockMovingBackwardsDoesNotInflateOrCrash()
        {
            var (s, c) = Make();
            s.Record("a");
            c.Now -= TimeSpan.FromDays(30);
            double score = s.GetScore("a");
            Assert.Equal(1.0, score, 9);
            s.Record("a");
            Assert.Equal(2.0, s.GetScore("a"), 9);
        }

        [Fact]
        public void NullEmptyAndHugeIdsAreIgnored()
        {
            var (s, _) = Make();
            s.Record(null);
            s.Record("");
            s.Record(new string('x', 5000));
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void PrunesLowestScoresFirst()
        {
            var (s, c) = Make(max: 3);
            s.Record("a"); s.Record("a"); s.Record("a");
            c.Now += TimeSpan.FromHours(1);
            s.Record("b"); s.Record("b");
            s.Record("c");
            s.Record("d");
            Assert.Equal(3, s.Count);
            Assert.Equal(0, s.GetScore("c"));
            Assert.True(s.GetScore("a") > 0);
            Assert.True(s.GetScore("d") > 0);
        }

        [Fact]
        public void PruneTieBreakIsDeterministic()
        {
            var (s, _) = Make(max: 2);
            s.Record("b"); s.Record("a"); s.Record("c");
            // All equal score and time: the ordinal-smallest key goes first.
            Assert.Equal(0, s.GetScore("a"));
            Assert.True(s.GetScore("b") > 0);
            Assert.True(s.GetScore("c") > 0);
        }

        [Fact]
        public void MaxEntriesIsAtLeastOne()
        {
            var (s, _) = Make(max: 0);
            s.Record("a"); s.Record("b");
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public void NonPositiveHalfLifeFallsBackToDefault()
        {
            var c = new Clock();
            var s = new FrecencyStore(c.Get, 10, TimeSpan.Zero);
            s.Record("a");
            c.Now += FrecencyStore.DefaultHalfLife;
            Assert.Equal(0.5, s.GetScore("a"), 9);
        }

        [Fact]
        public void NullClockThrows() => Assert.Throws<ArgumentNullException>(() => new FrecencyStore(null));

        [Fact]
        public void JsonRoundTripKeepsScores()
        {
            var (s, c) = Make();
            s.Record("a"); s.Record("a"); s.Record("b");
            c.Now += TimeSpan.FromDays(5);

            string json = s.ToJson();
            var findings = new List<string>();
            var loaded = FrecencyStore.FromJson(json, c.Get, findings, 500, TimeSpan.FromDays(10));

            Assert.Empty(findings);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(s.GetScore("a"), loaded.GetScore("a"), 9);
            Assert.Equal(s.GetScore("b"), loaded.GetScore("b"), 9);
            Assert.Equal(2, loaded.GetCount("a"));
        }

        [Fact]
        public void JsonIsStable()
        {
            var (s, _) = Make();
            s.Record("b"); s.Record("a");
            Assert.Equal(s.ToJson(), s.ToJson());
        }

        [Fact]
        public void JsonWithUnicodeIdsRoundTrips()
        {
            var (s, c) = Make();
            s.Record("アプリ:設定");
            var loaded = FrecencyStore.FromJson(s.ToJson(), c.Get);
            Assert.Equal(1, loaded.GetScore("アプリ:設定"), 9);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"entries\": 5}")]
        [InlineData("{\"foo\": 1}")]
        [InlineData("\"hello\"")]
        public void CorruptJsonYieldsEmptyStoreAndFinding(string json)
        {
            var findings = new List<string>();
            var s = FrecencyStore.FromJson(json, new Clock().Get, findings);
            Assert.Equal(0, s.Count);
            Assert.NotEmpty(findings);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyInputIsEmptyStoreWithoutFinding(string json)
        {
            var findings = new List<string>();
            var s = FrecencyStore.FromJson(json, new Clock().Get, findings);
            Assert.Equal(0, s.Count);
            Assert.Empty(findings);
        }

        [Fact]
        public void NullFindingsListIsAccepted()
        {
            Assert.Equal(0, FrecencyStore.FromJson("garbage", new Clock().Get, null).Count);
        }

        [Fact]
        public void BadEntriesAreSkippedGoodOnesKept()
        {
            string json = @"{""version"":1,""entries"":[
                {""id"":""ok"",""score"":2,""last"":""2025-01-01T00:00:00.0000000+00:00"",""count"":2},
                {""id"":"""",""score"":1,""last"":""2025-01-01T00:00:00Z""},
                {""id"":""neg"",""score"":-1,""last"":""2025-01-01T00:00:00Z""},
                {""id"":""nodate"",""score"":1},
                {""id"":""baddate"",""score"":1,""last"":""yesterday-ish""},
                {""id"":""str"",""score"":""x"",""last"":""2025-01-01T00:00:00Z""},
                5,
                {""id"":""huge"",""score"":1e300,""last"":""2025-01-01T00:00:00Z""}
            ],}";
            var findings = new List<string>();
            var s = FrecencyStore.FromJson(json, new Clock().Get, findings);
            Assert.Equal(1, s.Count);
            Assert.Equal(2, s.GetScore("ok"), 9);
            Assert.Equal(7, findings.Count);
        }

        [Fact]
        public void LoadingMoreThanMaxPrunes()
        {
            var (s, c) = Make(max: 10);
            for (int i = 0; i < 10; i++) s.Record("id" + i);
            var loaded = FrecencyStore.FromJson(s.ToJson(), c.Get, null, 4);
            Assert.Equal(4, loaded.Count);
        }

        [Fact]
        public void ClearEmptiesStore()
        {
            var (s, _) = Make();
            s.Record("a");
            s.Clear();
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void ConcurrentRecordsAreCounted()
        {
            var s = new FrecencyStore(() => new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Parallel.For(0, 1000, i => s.Record("k" + (i % 5)));
            for (int k = 0; k < 5; k++) Assert.Equal(200, s.GetCount("k" + k));
        }
    }
}
