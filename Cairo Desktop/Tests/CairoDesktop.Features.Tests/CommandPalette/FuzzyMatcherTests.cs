using System;
using System.Linq;
using CairoDesktop.Features.CommandPalette;
using Xunit;

namespace CairoDesktop.Features.Tests.CommandPalette
{
    public class FuzzyMatcherTests
    {
        [Fact]
        public void EmptyPattern_MatchesWithNoIndices()
        {
            var m = FuzzyMatcher.Match("", "anything");
            Assert.True(m.IsMatch);
            Assert.Empty(m.Indices);
        }

        [Fact]
        public void NullPattern_Matches()
        {
            Assert.True(FuzzyMatcher.Match(null, "x").IsMatch);
        }

        [Fact]
        public void EmptyOrNullText_NoMatch()
        {
            Assert.False(FuzzyMatcher.Match("a", "").IsMatch);
            Assert.False(FuzzyMatcher.Match("a", null).IsMatch);
        }

        [Fact]
        public void NonSubsequence_NoMatch()
        {
            Assert.False(FuzzyMatcher.Match("xyz", "Cairo Desktop").IsMatch);
            Assert.False(FuzzyMatcher.Match("ba", "ab").IsMatch);
        }

        [Fact]
        public void PatternLongerThanText_NoMatch()
        {
            Assert.False(FuzzyMatcher.Match("abcd", "abc").IsMatch);
        }

        [Fact]
        public void IsCaseInsensitive()
        {
            Assert.True(FuzzyMatcher.Match("CAIRO", "cairo desktop").IsMatch);
            Assert.True(FuzzyMatcher.Match("cairo", "CAIRO DESKTOP").IsMatch);
        }

        [Fact]
        public void ReportsAscendingIndices()
        {
            var m = FuzzyMatcher.Match("cdt", "Cairo Desktop");
            Assert.True(m.IsMatch);
            Assert.Equal(3, m.Indices.Length);
            Assert.True(m.Indices.SequenceEqual(m.Indices.OrderBy(i => i)));
            Assert.Equal(m.Indices.Length, m.Indices.Distinct().Count());
        }

        [Fact]
        public void IndicesPointAtMatchingCharacters()
        {
            const string text = "Open Settings Panel";
            var m = FuzzyMatcher.Match("osp", text);
            Assert.Equal("osp", new string(m.Indices.Select(i => char.ToLowerInvariant(text[i])).ToArray()));
        }

        [Fact]
        public void PrefersWordStarts()
        {
            var m = FuzzyMatcher.Match("cd", "Cairo Desktop");
            Assert.Equal(new[] { 0, 6 }, m.Indices);
        }

        [Fact]
        public void PrefersCamelCaseHumps()
        {
            var m = FuzzyMatcher.Match("ds", "CairoDesktopSettings".Substring(5));
            Assert.Equal(new[] { 0, 7 }, m.Indices);
        }

        [Fact]
        public void ConsecutiveBeatsScattered()
        {
            Assert.True(FuzzyMatcher.Match("set", "settings").Score > FuzzyMatcher.Match("set", "s_e_t").Score);
        }

        [Fact]
        public void PrefixBeatsMidWord()
        {
            Assert.True(FuzzyMatcher.Match("cal", "calendar").Score > FuzzyMatcher.Match("cal", "local").Score);
        }

        [Fact]
        public void EarlierMatchBeatsLater()
        {
            Assert.True(FuzzyMatcher.Match("x", "xray").Score > FuzzyMatcher.Match("x", "abcdefgxray").Score);
        }

        [Fact]
        public void SmallerGapBeatsLargerGap()
        {
            Assert.True(FuzzyMatcher.Match("ab", "a_b").Score > FuzzyMatcher.Match("ab", "a____b").Score);
        }

        [Fact]
        public void ExactCaseGetsSmallBonus()
        {
            Assert.True(FuzzyMatcher.Match("Cairo", "Cairo").Score > FuzzyMatcher.Match("cairo", "Cairo").Score);
        }

        [Fact]
        public void ExactMatchBeatsLongerCandidate()
        {
            Assert.True(FuzzyMatcher.Match("calc", "calc").Score > FuzzyMatcher.Match("calc", "calculator").Score
                        || FuzzyMatcher.Match("calc", "calc").Score == FuzzyMatcher.Match("calc", "calculator").Score);
        }

        [Fact]
        public void ScoreIsAtLeastOneForMatches()
        {
            Assert.True(FuzzyMatcher.Match("z", new string('a', 200) + "z").Score >= 1);
        }

        [Fact]
        public void IsDeterministic()
        {
            var a = FuzzyMatcher.Match("abc", "a-b-c abc");
            var b = FuzzyMatcher.Match("abc", "a-b-c abc");
            Assert.Equal(a.Score, b.Score);
            Assert.Equal(a.Indices, b.Indices);
        }

        [Fact]
        public void HandlesUnicodeLetters()
        {
            var m = FuzzyMatcher.Match("ünï", "Ünïcode Tool");
            Assert.True(m.IsMatch);
            Assert.Equal(new[] { 0, 1, 2 }, m.Indices);
        }

        [Fact]
        public void HandlesCjkAndEmojiWithoutThrowing()
        {
            Assert.True(FuzzyMatcher.Match("設定", "システム設定").IsMatch);
            var m = FuzzyMatcher.Match("a", "😀a😀");
            Assert.True(m.IsMatch);
        }

        [Fact]
        public void TurkishCultureDoesNotChangeResult()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                Assert.True(FuzzyMatcher.Match("i", "INFO").IsMatch);
                Assert.Equal(new[] { 0 }, FuzzyMatcher.Match("i", "INFO").Indices);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void VeryLongTextIsBoundedAndFast()
        {
            string text = new string('a', 100000) + "b";
            var m = FuzzyMatcher.Match("ab", text);
            // 'b' is beyond the considered prefix, so there is no match - and no hang.
            Assert.False(m.IsMatch);
        }

        [Fact]
        public void VeryLongPatternIsTruncated()
        {
            string text = new string('a', 200);
            var m = FuzzyMatcher.Match(new string('a', 5000), text);
            Assert.True(m.IsMatch);
            Assert.Equal(FuzzyMatcher.MaxPatternLength, m.Indices.Length);
        }

        [Fact]
        public void RepeatedCharactersPickBestAlignment()
        {
            var m = FuzzyMatcher.Match("aa", "xaxaa");
            Assert.Equal(new[] { 3, 4 }, m.Indices);
        }
    }
}
