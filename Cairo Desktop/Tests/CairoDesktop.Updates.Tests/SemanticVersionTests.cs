using System;
using Xunit;

namespace CairoDesktop.Updates.Tests
{
    public class SemanticVersionTests
    {
        [Theory]
        [InlineData("1.2.3", 1, 2, 3, "")]
        [InlineData("v1.2.3", 1, 2, 3, "")]
        [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
        [InlineData("1.2.3+abc", 1, 2, 3, "")]
        [InlineData("0.4.17.5", 0, 4, 17, "")]   // Nerdbank 4-part version
        [InlineData(" 10.0.0 ", 10, 0, 0, "")]
        public void Parses(string text, int major, int minor, int patch, string pre)
        {
            Assert.True(SemanticVersion.TryParse(text, out var v));
            Assert.Equal((major, minor, patch, pre), (v.Major, v.Minor, v.Patch, v.Prerelease));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("1.2.x")]
        [InlineData("01.2.3")]
        [InlineData("1.2.3-")]
        [InlineData("1.2.3-a..b")]
        [InlineData("1.2.3-01")]
        [InlineData("1.2.3.4.5")]
        [InlineData("-1.2.3")]
        [InlineData("99999999999.0.0")]
        public void RejectsInvalid(string? text)
        {
            Assert.False(SemanticVersion.TryParse(text, out _));
        }

        [Fact]
        public void ParseThrowsOnInvalid() => Assert.Throws<FormatException>(() => SemanticVersion.Parse("nope"));

        [Theory]
        [InlineData("1.0.0", "2.0.0")]
        [InlineData("1.2.0", "1.10.0")]
        [InlineData("1.0.9", "1.0.10")]
        [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
        [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
        [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
        [InlineData("1.0.0-rc.1", "1.0.0")]
        public void OrdersAscending(string lower, string higher)
        {
            var a = SemanticVersion.Parse(lower);
            var b = SemanticVersion.Parse(higher);
            Assert.True(a < b);
            Assert.True(b > a);
            Assert.True(a <= b);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void BuildMetadataAndFourthPartDoNotAffectEquality()
        {
            Assert.Equal(SemanticVersion.Parse("1.2.3+x"), SemanticVersion.Parse("1.2.3+y"));
            Assert.Equal(SemanticVersion.Parse("1.2.3.9"), SemanticVersion.Parse("1.2.3"));
            Assert.Equal(SemanticVersion.Parse("1.2.3").GetHashCode(), SemanticVersion.Parse("1.2.3+q").GetHashCode());
        }

        [Fact]
        public void ToStringRoundTrips() => Assert.Equal("1.2.3-rc.1", SemanticVersion.Parse("v1.2.3-rc.1+meta").ToString());
    }
}
