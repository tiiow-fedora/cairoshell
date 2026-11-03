using System;
using System.IO;
using CairoDesktop.Customization.Cli;
using CairoDesktop.Customization.Config;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class SettingsCliTests
    {
        private static ThemeCliContext Context(TempDir dir) => new ThemeCliContext
        {
            PackFolders = new[] { Path.Combine(dir.Path, "ThemePacks") },
            UserPacksFolder = Path.Combine(dir.Path, "ThemePacks"),
            ConfigFile = Path.Combine(dir.Path, "cairo-plus.json"),
            CurrentDirectory = dir.Path
        };

        private static int Run(string[] args, ThemeCliContext context, out string text)
        {
            var writer = new StringWriter();
            int code = SettingsCli.Run(args, context, writer);
            text = writer.ToString();
            return code;
        }

        [Theory]
        [InlineData("--export-settings", "export-settings")]
        [InlineData("/import-settings", "import-settings")]
        [InlineData("--IMPORT-SETTINGS=x.zip", "import-settings")]
        public void Finds_commands(string arg, string expected)
        {
            Assert.Equal(expected, SettingsCli.FindCommand(new[] { "a.exe", arg }));
        }

        [Theory]
        [InlineData("--export-theme")]
        [InlineData("--apply-theme")]
        [InlineData("export-settings")]
        public void Ignores_other_arguments(string arg)
        {
            Assert.Null(SettingsCli.FindCommand(new[] { arg }));
        }

        [Fact]
        public void Export_then_import_through_the_commands()
        {
            using (var a = new TempDir())
            using (var b = new TempDir())
            {
                a.Write("cairo-plus.json", "{ \"enabled\": true, \"theme\": \"Mine\" }");
                a.Write("ThemePacks/Mine/theme.json", "{ }");

                Assert.Equal(0, Run(new[] { "--export-settings", "out.zip" }, Context(a), out string exportText));
                Assert.Contains("Wrote", exportText);
                Assert.True(File.Exists(Path.Combine(a.Path, "out.zip")));

                string zip = Path.Combine(a.Path, "out.zip");
                Assert.Equal(0, Run(new[] { "--import-settings", zip, "--dry-run" }, Context(b), out string dryText));
                Assert.Contains("Dry run", dryText);
                Assert.False(File.Exists(Path.Combine(b.Path, "cairo-plus.json")));

                Assert.Equal(0, Run(new[] { "--import-settings", zip }, Context(b), out string importText));
                Assert.Contains("Installed theme pack 'Mine'", importText);
                Assert.Equal("Mine", ConfigLoader.LoadFile(Path.Combine(b.Path, "cairo-plus.json")).Config.Theme);
            }
        }

        [Fact]
        public void Export_uses_a_default_file_name()
        {
            using (var dir = new TempDir())
            {
                dir.Write("cairo-plus.json", "{ }");
                Assert.Equal(0, Run(new[] { "--export-settings" }, Context(dir), out _));
                Assert.True(File.Exists(Path.Combine(dir.Path, "cairo-plus-settings.zip")));
            }
        }

        [Fact]
        public void Import_reports_removed_sections_and_trust_keeps_them()
        {
            using (var dir = new TempDir())
            {
                string zip = Path.Combine(dir.Path, "b.zip");
                using (var a = new TempDir())
                {
                    a.Write("cairo-plus.json", "{ \"hooks\": { \"onStart\": [ { \"action\": \"run\", \"arg\": \"x\" } ] } }");
                    Run(new[] { "--export-settings", zip }, Context(a), out string exportText);
                    Assert.Contains("hooks", exportText);
                }

                Assert.Equal(0, Run(new[] { "--import-settings", zip }, Context(dir), out string text));
                Assert.Contains("Removed \"hooks\"", text);
                Assert.True(ConfigLoader.LoadFile(Path.Combine(dir.Path, "cairo-plus.json")).Config.Hooks == null);

                Assert.Equal(0, Run(new[] { "--import-settings", zip, "--trust-code" }, Context(dir), out string trusted));
                Assert.DoesNotContain("Removed", trusted);
                Assert.NotNull(ConfigLoader.LoadFile(Path.Combine(dir.Path, "cairo-plus.json")).Config.Hooks);
            }
        }

        [Fact]
        public void Reports_usage_errors()
        {
            using (var dir = new TempDir())
            {
                Assert.Equal(2, Run(new[] { "--import-settings" }, Context(dir), out _));
                Assert.Equal(2, Run(new[] { "--import-settings", "missing.zip" }, Context(dir), out string text));
                Assert.Contains("does not exist", text);
                Assert.Equal(1, Run(new[] { "--export-settings" }, Context(dir), out string noConfig));
                Assert.Contains("error", noConfig);
            }
        }
    }
}
