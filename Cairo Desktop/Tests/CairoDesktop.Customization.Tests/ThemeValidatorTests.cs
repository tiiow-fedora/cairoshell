using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class ThemeValidatorTests
    {
        private static List<ThemeIssue> Check(string manifest, params (string path, string content)[] files)
        {
            using (var dir = new TempDir())
            {
                dir.Write("Pack/theme.json", manifest);
                foreach (var file in files) dir.Write("Pack/" + file.path, file.content);
                return ThemeValidator.Validate(ThemePackLoader.LoadFolder(Path.Combine(dir.Path, "Pack")));
            }
        }

        private static bool Has(List<ThemeIssue> issues, IssueSeverity severity, string location, string text = "")
        {
            return issues.Any(i => i.Severity == severity && i.Location.StartsWith(location) && i.Message.Contains(text));
        }

        [Fact]
        public void Bundled_packs_have_no_errors_or_warnings()
        {
            var packs = ThemePackLoader.Discover(new[] { TestPaths.BundledPacks });
            Assert.True(packs.Count >= 4);

            foreach (var pack in packs)
            {
                var problems = ThemeValidator.Validate(pack).Where(i => i.Severity != IssueSeverity.Info).Select(i => pack.Id + " " + i).ToList();
                Assert.Empty(problems);
            }
        }

        [Fact]
        public void Resource_key_list_matches_Cairo_xaml()
        {
            var inXaml = new HashSet<string>(Regex.Matches(File.ReadAllText(TestPaths.CairoXaml), "x:Key=\"([A-Za-z0-9_]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));
            var missing = inXaml.Where(k => !ThemeSchema.ResourceKeys.Contains(k)).ToList();
            var stale = ThemeSchema.ResourceKeys.Where(k => !inXaml.Contains(k)).ToList();
            Assert.Empty(missing);
            Assert.Empty(stale);
        }

        [Fact]
        public void Every_palette_brush_key_exists_in_Cairo_xaml()
        {
            var expansion = PaletteExpander.Expand(new Dictionary<string, string> { ["background"] = "#101010" });
            var unknown = expansion.Brushes.Keys.Where(k => !ThemeSchema.ResourceKeys.Contains(k)).ToList();
            Assert.Empty(unknown);
        }

        [Theory]
        [InlineData("#3B82F6")]
        [InlineData("#FFD166")]
        [InlineData("#E64553")]
        [InlineData("#00B894")]
        [InlineData("#101010")]
        public void Derived_accent_text_is_the_more_readable_of_black_and_white(string accent)
        {
            var roles = PaletteExpander.Expand(new Dictionary<string, string> { ["accent"] = accent }).Roles;
            var chosen = ColorParser.ContrastRatio(roles["accent"], roles["accentText"]);
            var other = ColorParser.ContrastRatio(roles["accent"], roles["accentText"] == System.Windows.Media.Colors.Black ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black);
            Assert.True(chosen >= other);
        }

        [Fact]
        public void Contrast_ratio_matches_the_wcag_extremes()
        {
            Assert.Equal(21.0, ColorParser.ContrastRatio(System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White), 1);
            Assert.Equal(1.0, ColorParser.ContrastRatio(System.Windows.Media.Colors.Red, System.Windows.Media.Colors.Red), 3);
        }

        [Fact]
        public void Broken_manifest_is_one_error()
        {
            var issues = Check("{ \"name\": ");
            Assert.Single(issues);
            Assert.Equal(IssueSeverity.Error, issues[0].Severity);
        }

        [Fact]
        public void Minimal_pack_is_fine()
        {
            var issues = Check("{ \"name\": \"X\", \"description\": \"d\", \"palette\": { \"background\": \"#202020\" } }");
            Assert.Empty(issues.Where(i => i.Severity != IssueSeverity.Info));
        }

        [Fact]
        public void Reports_bad_palette_values()
        {
            var issues = Check("{ \"palette\": { \"background\": \"#202020\", \"text\": \"white\", \"sparkle\": \"#FFF\" } }");
            Assert.True(Has(issues, IssueSeverity.Warning, "palette", "not a colour"));
            Assert.True(Has(issues, IssueSeverity.Warning, "palette", "Unknown palette role"));
        }

        [Fact]
        public void Reports_low_contrast_text()
        {
            var issues = Check("{ \"palette\": { \"background\": \"#303030\", \"text\": \"#404040\" } }");
            Assert.True(Has(issues, IssueSeverity.Warning, "palette.text", "contrast"));
        }

        [Fact]
        public void Reports_dark_mode_that_disagrees_with_background()
        {
            var issues = Check("{ \"darkMode\": true, \"palette\": { \"background\": \"#F5F5F5\", \"text\": \"#111111\" } }");
            Assert.True(Has(issues, IssueSeverity.Warning, "darkMode"));
        }

        [Fact]
        public void Reports_missing_files_and_escaping_paths()
        {
            var issues = Check(@"{
  ""wallpaper"": { ""file"": ""missing.jpg"", ""style"": ""zoom"" },
  ""font"": { ""family"": ""X"", ""file"": ""../outside.ttf"" },
  ""icons"": { ""NotAnIcon"": ""icons/a.png"", ""MenuIcon"": ""icons/menu.png"" },
  ""xaml"": ""extra.xaml""
}", ("icons/menu.png", "x"));
            Assert.True(Has(issues, IssueSeverity.Error, "wallpaper.file", "not found"));
            Assert.True(Has(issues, IssueSeverity.Warning, "wallpaper.style"));
            Assert.True(Has(issues, IssueSeverity.Error, "font.file", "outside"));
            Assert.True(Has(issues, IssueSeverity.Warning, "icons.NotAnIcon", "not a Cairo icon key"));
            Assert.True(Has(issues, IssueSeverity.Error, "icons.NotAnIcon", "not found"));
            Assert.False(Has(issues, IssueSeverity.Error, "icons.MenuIcon"));
            Assert.True(Has(issues, IssueSeverity.Error, "xaml", "not found"));
        }

        [Fact]
        public void Reports_unknown_resource_keys()
        {
            var issues = Check("{ \"resources\": { \"MenuTopBorderBrush\": \"#FF0000\", \"MenuTopBordrBrush\": \"#00FF00\" } }");
            Assert.True(Has(issues, IssueSeverity.Warning, "resources.MenuTopBordrBrush"));
            Assert.False(Has(issues, IssueSeverity.Warning, "resources.MenuTopBorderBrush"));
        }

        [Fact]
        public void Checks_bar_numbers_and_layout()
        {
            var issues = Check(@"{
  ""menuBar"": {
    ""opacity"": 1.5, ""margin"": 6, ""blur"": true, ""cornerRadius"": 8,
    ""layout"": { ""left"": [""cairoMenu"", ""cairoMenu""], ""center"": [""widget:clock"", ""widget:nope""], ""right"": [""clock"", ""widget:clock""] }
  },
  ""taskbar"": { ""layout"": { ""left"": [""startButton""] } },
  ""widgets"": { ""clock"": { ""type"": ""clock"" } }
}");
            Assert.True(Has(issues, IssueSeverity.Error, "menuBar.opacity"));
            Assert.True(Has(issues, IssueSeverity.Info, "menuBar.margin", "floating"));
            Assert.True(Has(issues, IssueSeverity.Info, "menuBar.blur"));
            Assert.True(Has(issues, IssueSeverity.Warning, "menuBar.layout.left", "listed twice"));
            Assert.True(Has(issues, IssueSeverity.Warning, "menuBar.layout.center", "widget:nope"));
            Assert.False(Has(issues, IssueSeverity.Warning, "menuBar.layout.center", "widget:clock"));
            Assert.True(Has(issues, IssueSeverity.Warning, "taskbar.layout.left", "startButton"));
        }

        [Fact]
        public void Checks_widgets()
        {
            var issues = Check(@"{
  ""widgets"": {
    ""a"": { ""type"": ""command"" },
    ""b"": { ""type"": ""teleporter"" },
    ""c"": 5,
    ""cpu"": { ""onClick"": { ""action"": ""launch"" } }
  }
}");
            Assert.True(Has(issues, IssueSeverity.Error, "widgets.a", "needs a \"command\""));
            Assert.True(Has(issues, IssueSeverity.Warning, "widgets.b", "teleporter"));
            Assert.True(Has(issues, IssueSeverity.Error, "widgets.c", "object"));
            Assert.True(Has(issues, IssueSeverity.Error, "widgets.cpu.onClick", "needs an \"arg\""));
        }

        [Fact]
        public void Checks_hooks()
        {
            var issues = Check(@"{
  ""hooks"": { ""onThemeApplied"": [ { ""action"": ""theme"", ""arg"": ""next"" }, { ""action"": ""run"", ""arg"": ""echo hi"" }, { ""action"": ""run"" } ] }
}");
            Assert.True(Has(issues, IssueSeverity.Info, "hooks", "allowPackHooks"));
            Assert.True(Has(issues, IssueSeverity.Error, "hooks.onThemeApplied[0]", "unknown hook action"));
            Assert.False(Has(issues, IssueSeverity.Error, "hooks.onThemeApplied[1]"));
            Assert.True(Has(issues, IssueSeverity.Error, "hooks.onThemeApplied[2]", "needs an \"arg\""));
        }
    }
}
