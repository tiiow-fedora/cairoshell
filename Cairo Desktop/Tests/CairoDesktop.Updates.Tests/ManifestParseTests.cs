using System;
using System.Text;
using Xunit;

namespace CairoDesktop.Updates.Tests
{
    public class ManifestParseTests
    {
        private static UpdateManifest Parse(string json) => UpdateManifest.Parse(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void ParsesFullManifest()
        {
            var m = UpdateManifest.Parse(ManifestBuilder.Json("0.5.0", minimum: "0.3.0"));
            Assert.Equal(SemanticVersion.Parse("0.5.0"), m.Version);
            Assert.Equal("stable", m.Channel);
            Assert.Equal(SemanticVersion.Parse("0.3.0"), m.MinimumVersion);
            Assert.Single(m.Assets);
            Assert.Equal("x64", m.Assets[0].Platform);
            Assert.Equal(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), m.Published.UtcDateTime);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData(@"{""schemaVersion"":2,""version"":""1.0.0"",""published"":""2026-01-01"",""assets"":[]}")]
        [InlineData(@"{""schemaVersion"":1,""version"":""bad"",""published"":""2026-01-01"",""assets"":[]}")]
        [InlineData(@"{""schemaVersion"":1,""version"":""1.0.0"",""published"":""yesterday"",""assets"":[]}")]
        [InlineData(@"{""schemaVersion"":1,""version"":""1.0.0"",""published"":""2026-01-01"",""assets"":[]}")]
        [InlineData(@"{""schemaVersion"":1,""version"":""1.0.0"",""published"":""2026-01-01""}")]
        public void RejectsMalformed(string json)
        {
            Assert.Throws<UpdateException>(() => Parse(json));
        }

        [Fact]
        public void RejectsHttpAssetUrl()
        {
            var json = Encoding.UTF8.GetString(ManifestBuilder.Json(url: "http://updates.example.com/x.exe"));
            Assert.Throws<UpdateException>(() => Parse(json));
        }

        [Fact]
        public void RejectsNonHttpScheme()
        {
            var json = Encoding.UTF8.GetString(ManifestBuilder.Json(url: "file:///C:/x.exe"));
            Assert.Throws<UpdateException>(() => Parse(json));
        }

        [Fact]
        public void RejectsBadHashAndSize()
        {
            Assert.Throws<UpdateException>(() => UpdateManifest.Parse(ManifestBuilder.Json(sha: "1234")));
            Assert.Throws<UpdateException>(() => UpdateManifest.Parse(ManifestBuilder.Json(size: 0)));
        }

        [Fact]
        public void RejectsComments()
        {
            Assert.Throws<UpdateException>(() => Parse("{ /* hi */ \"schemaVersion\": 1 }"));
        }
    }
}
