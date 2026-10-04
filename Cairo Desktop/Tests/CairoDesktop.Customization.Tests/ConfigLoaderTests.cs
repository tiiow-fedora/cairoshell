using System;
using System.IO;
using CairoDesktop.Customization.Config;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class ConfigLoaderTests
    {
        [Fact]
        public void Parses_comments_and_trailing_commas()
        {
            const string json = @"
// leading line comment
{
  /* block comment */
  ""enabled"": true, // trailing comment
  ""theme"": ""Nord"",
  ""hotkeys"": [
    { ""keys"": ""Win+Alt+T"", ""action"": ""theme"", ""arg"": ""next"", },
  ],
}";
            var result = ConfigLoader.Parse(json);

            Assert.True(result.Success, result.Error);
            Assert.True(result.Config.Enabled);
            Assert.Equal("Nord", result.Config.Theme);
            Assert.Single(result.Config.Hotkeys);
            Assert.Equal("Win+Alt+T", result.Config.Hotkeys[0].Keys);
        }

        [Fact]
        public void Property_names_are_case_insensitive()
        {
            var result = ConfigLoader.Parse(@"{ ""Enabled"": true, ""THEME"": ""Gruvbox"" }");

            Assert.True(result.Config.Enabled);
            Assert.Equal("Gruvbox", result.Config.Theme);
        }

        [Fact]
        public void Missing_file_yields_disabled_default()
        {
            string path = Path.Combine(Path.GetTempPath(), "cairo-plus-missing-" + Guid.NewGuid().ToString("N") + ".json");

            var result = ConfigLoader.LoadFile(path);

            Assert.True(result.Success);
            Assert.False(result.FileExists);
            Assert.NotNull(result.Config);
            Assert.False(result.Config.Enabled);
            Assert.Null(result.Config.Theme);
        }

        [Fact]
        public void Empty_file_is_treated_as_missing()
        {
            var result = ConfigLoader.Parse("   \r\n  ");

            Assert.True(result.Success);
            Assert.False(result.Config.Enabled);
        }

        [Fact]
        public void Enabled_false_disables_everything_even_with_settings_present()
        {
            var result = ConfigLoader.Parse(@"{ ""enabled"": false, ""theme"": ""Nord"", ""taskbar"": { ""cornerRadius"": 8 } }");

            Assert.True(result.Success);
            Assert.False(result.Config.Enabled);

            var resolved = ResolvedSettings.Resolve(result.Config, null);
            Assert.False(resolved.Enabled);
            Assert.Null(resolved.Pack);
            Assert.Null(resolved.Taskbar);
        }

        [Fact]
        public void Enabled_defaults_to_false_when_omitted()
        {
            var result = ConfigLoader.Parse(@"{ ""theme"": ""Nord"" }");

            Assert.False(result.Config.Enabled);
        }

        [Fact]
        public void Syntax_error_reports_line_and_returns_disabled_config()
        {
            var result = ConfigLoader.Parse("{\n  \"enabled\": true\n  \"theme\": \"Nord\"\n}");

            Assert.False(result.Success);
            Assert.Contains("line 3", result.Error);
            Assert.False(result.Config.Enabled);
        }

        [Fact]
        public void Loads_from_disk_with_utf8_bom()
        {
            string path = Path.Combine(Path.GetTempPath(), "cairo-plus-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{ \"enabled\": true, // on\n \"theme\": \"TokyoNight\", }", new System.Text.UTF8Encoding(true));

                var result = ConfigLoader.LoadFile(path);

                Assert.True(result.Success, result.Error);
                Assert.True(result.FileExists);
                Assert.Equal("TokyoNight", result.Config.Theme);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void User_config_overrides_theme_pack_bar_settings_field_by_field()
        {
            var pack = new BarConfig { CornerRadius = 10, Floating = true, Opacity = 0.8 };
            var user = new BarConfig { CornerRadius = 4 };

            var merged = BarConfig.Merge(pack, user);

            Assert.Equal(4, merged.CornerRadius);
            Assert.True(merged.Floating);
            Assert.Equal(0.8, merged.Opacity);
        }
    }
}
