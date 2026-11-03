using System.IO;
using System.Linq;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class ThemeFindingsTests
    {
        private static ThemeFindings Check(string manifest, params (string path, string content)[] files)
        {
            using (var dir = new TempDir())
            {
                dir.Write("Pack/theme.json", manifest);
                foreach (var file in files) dir.Write("Pack/" + file.path, file.content);
                return ThemeFindings.For(ThemePackLoader.LoadFolder(Path.Combine(dir.Path, "Pack")));
            }
        }

        [Fact]
        public void Lines_are_ordered_by_severity_and_keep_discovery_order_within_one()
        {
            var findings = new ThemeFindings("P", "Pack", new[]
            {
                new ThemeIssue(IssueSeverity.Info, "name", "a"),
                new ThemeIssue(IssueSeverity.Warning, "w1", "b"),
                new ThemeIssue(IssueSeverity.Error, "e1", "c"),
                new ThemeIssue(IssueSeverity.Warning, "w2", "d"),
                new ThemeIssue(IssueSeverity.Error, "e2", "e"),
            });

            Assert.Equal(new[] { "e1", "e2", "w1", "w2", "name" }, findings.Lines.Select(l => l.Location).ToArray());
            Assert.Equal(2, findings.ErrorCount);
            Assert.Equal(2, findings.WarningCount);
            Assert.Equal(1, findings.InfoCount);
            Assert.True(findings.HasErrors);
            Assert.False(findings.IsClean);
        }

        [Theory]
        [InlineData(0, 0, 0, "No problems")]
        [InlineData(0, 0, 3, "No problems (3 notes)")]
        [InlineData(0, 0, 1, "No problems (1 note)")]
        [InlineData(1, 0, 0, "1 error")]
        [InlineData(0, 2, 5, "2 warnings")]
        [InlineData(2, 1, 0, "2 errors, 1 warning")]
        public void Summary_wording(int errors, int warnings, int infos, string expected)
        {
            var issues = Enumerable.Repeat(new ThemeIssue(IssueSeverity.Error, "x", "m"), errors)
                .Concat(Enumerable.Repeat(new ThemeIssue(IssueSeverity.Warning, "x", "m"), warnings))
                .Concat(Enumerable.Repeat(new ThemeIssue(IssueSeverity.Info, "x", "m"), infos));
            Assert.Equal(expected, new ThemeFindings("P", "Pack", issues).Summary);
        }

        [Fact]
        public void Text_hides_notes_unless_asked_and_handles_missing_location()
        {
            var findings = new ThemeFindings("P", "My Pack", new[]
            {
                new ThemeIssue(IssueSeverity.Error, "", "theme.json could not be read."),
                new ThemeIssue(IssueSeverity.Warning, "palette.text", "low contrast"),
                new ThemeIssue(IssueSeverity.Info, "name", "not set"),
            });

            Assert.Equal("My Pack: 1 error, 1 warning\nError  theme.json could not be read.\nWarning  palette.text: low contrast", findings.ToText());
            Assert.EndsWith("Info  name: not set", findings.ToText(includeInfo: true));
        }

        [Fact]
        public void Empty_findings_are_clean()
        {
            var findings = new ThemeFindings("P", "Pack", null);
            Assert.True(findings.IsClean);
            Assert.Empty(findings.Lines);
            Assert.Equal("Pack: No problems", findings.ToText());
        }

        [Fact]
        public void Findings_for_a_real_pack_report_what_the_validator_finds()
        {
            var findings = Check(@"{ ""name"": ""Bad"", ""palette"": { ""background"": ""#101010"", ""text"": ""#181818"" }, ""wallpaper"": { ""file"": ""missing.jpg"" } }");

            Assert.True(findings.HasErrors);
            Assert.Equal("Error", findings.Lines[0].SeverityLabel);
            Assert.Equal("wallpaper.file", findings.Lines[0].Location);
            Assert.Contains(findings.Lines, l => l.Severity == IssueSeverity.Warning && l.Location == "palette.text");
            Assert.Equal("Bad", findings.PackName);
            Assert.Equal("Pack", findings.PackId);
        }

        [Fact]
        public void Unreadable_manifest_is_one_error()
        {
            var findings = Check("{ not json");
            Assert.Equal(1, findings.ErrorCount);
            Assert.Equal(1, findings.Lines.Count);
            Assert.Equal("", findings.Lines[0].Location);
        }

        [Fact]
        public void Variants_are_checked_as_applied()
        {
            // Light variant has unreadable text on its own background; the dark base is fine.
            var findings = Check(@"{
  ""name"": ""V"",
  ""palette"": { ""background"": ""#101010"", ""text"": ""#EEEEEE"" },
  ""light"": { ""palette"": { ""background"": ""#F0F0F0"", ""text"": ""#E0E0E0"" }, ""wallpaper"": { ""file"": ""day.jpg"" } }
}");
            Assert.Contains(findings.Lines, l => l.Severity == IssueSeverity.Warning && l.Location == "light.palette.text");
            Assert.Contains(findings.Lines, l => l.Severity == IssueSeverity.Error && l.Location == "light.wallpaper.file");
            Assert.False(findings.Lines.Any(l => l.Location.StartsWith("dark")));
        }

        [Fact]
        public void Light_variant_with_a_dark_background_is_flagged()
        {
            var findings = Check(@"{ ""name"": ""V"", ""palette"": { ""background"": ""#101010"" }, ""light"": { ""palette"": { ""accent"": ""#FF0000"" } } }");
            Assert.Contains(findings.Lines, l => l.Severity == IssueSeverity.Warning && l.Location == "light.darkMode");
        }

        [Fact]
        public void Empty_variant_is_a_note()
        {
            var findings = Check(@"{ ""name"": ""V"", ""palette"": { ""background"": ""#101010"" }, ""dark"": { } }");
            Assert.Contains(findings.Lines, l => l.Severity == IssueSeverity.Info && l.Location == "dark");
            Assert.True(findings.IsClean);
        }

        [Fact]
        public void Base_problems_are_not_repeated_for_variants()
        {
            var findings = Check(@"{ ""name"": ""V"", ""palette"": { ""background"": ""#101010"", ""text"": ""#181818"" }, ""dark"": { ""palette"": { ""accent"": ""#FFFFFF"" } } }");
            Assert.False(findings.Lines.Any(l => l.Location.StartsWith("dark.palette.text")));
            Assert.Contains(findings.Lines, l => l.Location == "palette.text");
        }
    }
}
