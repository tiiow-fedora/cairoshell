using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Settings;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class SettingsBundleTests
    {
        private const string ManifestJson = "{\"format\":\"cairo-plus-settings\",\"version\":1}";
        private const string PackJson = "{ \"name\": \"Mine\" }";

        private static string MakeZip(TempDir dir, params (string Name, string Content)[] entries)
        {
            string path = System.IO.Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (name, content) in entries)
                {
                    var entry = zip.CreateEntry(name);
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(content);
                }
            }

            return path;
        }

        /// <summary>A "machine": a config file and a user packs folder inside a fresh temp folder.</summary>
        private sealed class Machine : IDisposable
        {
            private readonly TempDir _dir = new TempDir();
            public string Config => System.IO.Path.Combine(_dir.Path, "cairo-plus.json");
            public string Packs => System.IO.Path.Combine(_dir.Path, "ThemePacks");
            public string Zip => System.IO.Path.Combine(_dir.Path, "bundle.zip");
            public TempDir Dir => _dir;

            public Machine(string config = null)
            {
                if (config != null) File.WriteAllText(Config, config);
            }

            public void AddPack(string name, string json = PackJson, string extraFile = null)
            {
                _dir.Write("ThemePacks/" + name + "/theme.json", json);
                if (extraFile != null) _dir.Write("ThemePacks/" + name + "/" + extraFile, "data");
            }

            public void Dispose() => _dir.Dispose();
        }

        // ------------------------------------------------------------ export

        [Fact]
        public void Export_keeps_config_text_and_includes_user_packs()
        {
            using (var m = new Machine("// my note\n{ \"enabled\": true, \"theme\": \"Mine\", }"))
            {
                m.AddPack("Mine", extraFile: "icons/a.png");
                Directory.CreateDirectory(System.IO.Path.Combine(m.Packs, "NotAPack")); // no theme.json: skipped

                var result = SettingsBundle.Export(m.Config, m.Packs, m.Zip);
                Assert.True(result.Success, result.Error);

                using (var zip = ZipFile.OpenRead(m.Zip))
                {
                    var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                    Assert.Equal(new[] { "ThemePacks/Mine/icons/a.png", "ThemePacks/Mine/theme.json", "cairo-plus.json", "manifest.json" }.OrderBy(n => n, StringComparer.Ordinal), names);
                    using (var reader = new StreamReader(zip.GetEntry("cairo-plus.json").Open()))
                    {
                        Assert.StartsWith("// my note", reader.ReadToEnd());
                    }
                }
            }
        }

        [Fact]
        public void Export_without_packs_option_leaves_packs_out()
        {
            using (var m = new Machine("{ \"enabled\": true }"))
            {
                m.AddPack("Mine");
                var result = SettingsBundle.Export(m.Config, m.Packs, m.Zip, new ExportOptions { IncludePacks = false });
                Assert.True(result.Success, result.Error);
                Assert.Empty(SettingsBundle.Analyze(m.Zip).Info.Packs);
            }
        }

        [Fact]
        public void Export_refuses_missing_or_broken_config()
        {
            using (var m = new Machine())
            {
                Assert.False(SettingsBundle.Export(m.Config, m.Packs, m.Zip).Success);
                File.WriteAllText(m.Config, "{ \"enabled\": ");
                var result = SettingsBundle.Export(m.Config, m.Packs, m.Zip);
                Assert.False(result.Success);
                Assert.Contains("syntax error", result.Error);
            }
        }

        [Fact]
        public void Export_warns_about_code_running_sections()
        {
            using (var m = new Machine("{ \"hotkeys\": [ { \"keys\": \"Ctrl+F1\", \"action\": \"launch\", \"arg\": \"calc.exe\" } ] }"))
            {
                var result = SettingsBundle.Export(m.Config, m.Packs, m.Zip);
                Assert.True(result.Success);
                Assert.Contains(result.Warnings, w => w.Contains("hotkeys"));
            }
        }

        [Fact]
        public void Manifest_records_version_and_time()
        {
            using (var m = new Machine("{ \"theme\": \"Nord\" }"))
            {
                SettingsBundle.Export(m.Config, m.Packs, m.Zip, new ExportOptions { Now = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc) });
                var manifest = SettingsBundle.Analyze(m.Zip).Info.Manifest;
                Assert.Equal(BundleManifest.CurrentVersion, manifest.Version);
                Assert.Equal("2026-05-06T07:08:09Z", manifest.CreatedUtc);
                Assert.Equal("Nord", manifest.Theme);
            }
        }

        // ------------------------------------------------------------ round trip

        [Fact]
        public void Round_trip_moves_config_and_packs_to_another_machine()
        {
            const string config = "// keep me\n{ \"enabled\": true, \"theme\": \"Mine\", \"menuBar\": { \"opacity\": 0.5 } }";
            using (var a = new Machine(config))
            using (var b = new Machine())
            {
                a.AddPack("Mine", extraFile: "wallpaper.jpg");
                Assert.True(SettingsBundle.Export(a.Config, a.Packs, a.Zip).Success);

                var result = SettingsBundle.Import(a.Zip, b.Config, b.Packs);
                Assert.True(result.Success, result.Error);
                Assert.Equal(config, File.ReadAllText(b.Config));
                Assert.Equal(new[] { "Mine" }, result.InstalledPacks);
                Assert.True(File.Exists(System.IO.Path.Combine(b.Packs, "Mine", "wallpaper.jpg")));
                Assert.Null(result.BackupPath);
                Assert.Empty(result.Warnings);
                Assert.DoesNotContain(Directory.GetDirectories(b.Packs), d => System.IO.Path.GetFileName(d).StartsWith("."));
            }
        }

        [Fact]
        public void Import_backs_up_the_existing_config()
        {
            using (var m = new Machine("{ \"enabled\": false, \"theme\": \"Old\" }"))
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ \"enabled\": true }"));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs, new ImportOptions { Now = new DateTime(2026, 1, 2, 3, 4, 5) });

                Assert.True(result.Success, result.Error);
                Assert.EndsWith(".bak-20260102030405", result.BackupPath);
                Assert.Equal("{ \"enabled\": false, \"theme\": \"Old\" }", File.ReadAllText(result.BackupPath));
                Assert.Equal("{ \"enabled\": true }", File.ReadAllText(m.Config));
            }
        }

        [Fact]
        public void Import_does_not_overwrite_an_earlier_backup()
        {
            using (var m = new Machine("{ }"))
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ \"enabled\": true }"));
                var options = new ImportOptions { Now = new DateTime(2026, 1, 2, 3, 4, 5) };
                var first = SettingsBundle.Import(zip, m.Config, m.Packs, options);
                var second = SettingsBundle.Import(zip, m.Config, m.Packs, options);
                Assert.NotEqual(first.BackupPath, second.BackupPath);
                Assert.Equal("{ }", File.ReadAllText(first.BackupPath));
            }
        }

        // ------------------------------------------------------------ trust

        private const string RiskyConfig = @"{
  ""enabled"": true,
  ""theme"": ""Nord"",
  ""allowPackHooks"": true,
  ""hooks"": { ""onStart"": [ { ""action"": ""run"", ""arg"": ""evil.cmd"" } ] },
  ""hotkeys"": [ { ""keys"": ""Ctrl+F1"", ""action"": ""launch"", ""arg"": ""calc.exe"" } ],
  ""widgets"": { ""x"": { ""type"": ""command"", ""command"": ""whoami"" }, ""clock"": { } },
  ""menuBar"": { ""opacity"": 0.5 }
}";

        [Fact]
        public void Detects_code_running_sections()
        {
            var config = ConfigLoader.Parse(RiskyConfig).Config;
            Assert.Equal(new[] { "hooks", "hotkeys", "widgets", "allowPackHooks" }, SettingsBundle.FindExecutableSections(config));
        }

        [Fact]
        public void Safe_config_has_no_executable_sections()
        {
            var config = ConfigLoader.Parse(@"{ ""hotkeys"": [ { ""keys"": ""Ctrl+F1"", ""action"": ""theme"", ""arg"": ""next"" } ], ""widgets"": { ""clock"": { }, ""c"": { ""type"": ""cpu"" } } }").Config;
            Assert.Empty(SettingsBundle.FindExecutableSections(config));
        }

        [Fact]
        public void A_command_widget_is_found_by_type_or_by_id()
        {
            Assert.Contains("widgets", SettingsBundle.FindExecutableSections(ConfigLoader.Parse(@"{ ""widgets"": { ""Command"": { } } }").Config));
            Assert.Contains("widgets", SettingsBundle.FindExecutableSections(ConfigLoader.Parse(@"{ ""widgets"": { ""a"": { ""Type"": ""COMMAND"" } } }").Config));
        }

        [Fact]
        public void Import_drops_code_running_sections_by_default()
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", RiskyConfig));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs);
                Assert.True(result.Success, result.Error);
                Assert.Equal(new[] { "hooks", "hotkeys", "widgets", "allowPackHooks" }, result.Dropped);

                var loaded = ConfigLoader.LoadFile(m.Config);
                Assert.True(loaded.Success, loaded.Error);
                Assert.Empty(SettingsBundle.FindExecutableSections(loaded.Config));
                Assert.False(loaded.Config.AllowPackHooks);
                Assert.Equal("Nord", loaded.Config.Theme);
                Assert.Equal(0.5, loaded.Config.MenuBar.Opacity);
            }
        }

        [Fact]
        public void Import_keeps_code_running_sections_when_trusted()
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", RiskyConfig));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs, new ImportOptions { TrustExecutable = true });
                Assert.True(result.Success, result.Error);
                Assert.Empty(result.Dropped);
                Assert.Equal(RiskyConfig, File.ReadAllText(m.Config));
                Assert.Contains(result.Warnings, w => w.StartsWith("Trusted"));
            }
        }

        // ------------------------------------------------------------ packs

        [Fact]
        public void Identical_existing_pack_is_skipped()
        {
            using (var a = new Machine("{ \"theme\": \"Mine\" }"))
            using (var b = new Machine())
            {
                a.AddPack("Mine", extraFile: "x.txt");
                b.AddPack("Mine", extraFile: "x.txt");
                SettingsBundle.Export(a.Config, a.Packs, a.Zip);

                var result = SettingsBundle.Import(a.Zip, b.Config, b.Packs);
                Assert.True(result.Success, result.Error);
                Assert.Equal(new[] { "Mine" }, result.SkippedPacks);
                Assert.Empty(result.InstalledPacks);
                Assert.Equal(new[] { "Mine" }, Directory.GetDirectories(b.Packs).Select(System.IO.Path.GetFileName));
            }
        }

        [Fact]
        public void Different_existing_pack_is_kept_and_the_incoming_one_renamed()
        {
            using (var a = new Machine("{ \"enabled\": true, \"theme\": \"Mine\" }"))
            using (var b = new Machine())
            {
                a.AddPack("Mine", "{ \"name\": \"Theirs\" }");
                b.AddPack("Mine", "{ \"name\": \"Local\" }");
                SettingsBundle.Export(a.Config, a.Packs, a.Zip);

                var result = SettingsBundle.Import(a.Zip, b.Config, b.Packs);
                Assert.True(result.Success, result.Error);
                Assert.Equal("Mine-2", result.RenamedPacks["Mine"]);
                Assert.Contains("Local", File.ReadAllText(System.IO.Path.Combine(b.Packs, "Mine", "theme.json")));
                Assert.Contains("Theirs", File.ReadAllText(System.IO.Path.Combine(b.Packs, "Mine-2", "theme.json")));
                Assert.Equal("Mine-2", ConfigLoader.LoadFile(b.Config).Config.Theme);
            }
        }

        [Fact]
        public void Import_can_leave_packs_out()
        {
            using (var a = new Machine("{ \"theme\": \"Mine\" }"))
            using (var b = new Machine())
            {
                a.AddPack("Mine");
                SettingsBundle.Export(a.Config, a.Packs, a.Zip);
                var result = SettingsBundle.Import(a.Zip, b.Config, b.Packs, new ImportOptions { ImportPacks = false });
                Assert.True(result.Success, result.Error);
                Assert.False(Directory.Exists(b.Packs));
                Assert.Contains(result.Warnings, w => w.Contains("not installed"));
            }
        }

        [Fact]
        public void A_theme_in_a_bundled_folder_does_not_warn()
        {
            using (var m = new Machine())
            using (var other = new Machine())
            {
                other.AddPack("Nord");
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ \"theme\": \"Nord\" }"));

                Assert.Contains(SettingsBundle.Import(zip, m.Config, m.Packs, new ImportOptions { DryRun = true }).Warnings, w => w.Contains("not installed"));
                Assert.DoesNotContain(SettingsBundle.Import(zip, m.Config, m.Packs, new ImportOptions { DryRun = true, OtherPackFolders = new[] { other.Packs } }).Warnings, w => w.Contains("not installed"));
            }
        }

        [Fact]
        public void Dry_run_writes_nothing()
        {
            using (var a = new Machine("{ \"enabled\": true }"))
            using (var b = new Machine("{ \"enabled\": false }"))
            {
                a.AddPack("Mine");
                SettingsBundle.Export(a.Config, a.Packs, a.Zip);

                var result = SettingsBundle.Import(a.Zip, b.Config, b.Packs, new ImportOptions { DryRun = true });
                Assert.True(result.Success, result.Error);
                Assert.Equal(new[] { "Mine" }, result.InstalledPacks);
                Assert.Equal("{ \"enabled\": false }", File.ReadAllText(b.Config));
                Assert.False(Directory.Exists(b.Packs));
                Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(b.Config), "*.bak-*"));
            }
        }

        [Fact]
        public void Config_problems_are_reported_as_warnings()
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ \"appearance\": \"purple\" }"));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs);
                Assert.True(result.Success);
                Assert.Contains(result.Warnings, w => w.Contains("appearance"));
            }
        }

        // ------------------------------------------------------------ refusals

        [Fact]
        public void Refuses_path_traversal()
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ }"), ("ThemePacks/Mine/theme.json", PackJson), ("ThemePacks/Mine/../../escape.txt", "x"));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs);
                Assert.False(result.Success);
                Assert.Contains("..", result.Error);
                Assert.False(File.Exists(m.Config));
                Assert.False(File.Exists(System.IO.Path.Combine(m.Dir.Path, "escape.txt")));
            }
        }

        [Theory]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Windows/x.txt")]
        [InlineData("notes.txt")]
        [InlineData("Widgets/evil.dll")]
        public void Refuses_absolute_paths_and_stray_entries(string name)
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ }"), (name, "x"));
                Assert.False(SettingsBundle.Import(zip, m.Config, m.Packs).Success);
                Assert.False(File.Exists(m.Config));
            }
        }

        [Fact]
        public void Refuses_a_newer_bundle_version()
        {
            using (var m = new Machine("{ \"keep\": true }"))
            {
                string zip = MakeZip(m.Dir, ("manifest.json", "{\"format\":\"cairo-plus-settings\",\"version\":99}"), ("cairo-plus.json", "{ }"));
                var result = SettingsBundle.Import(zip, m.Config, m.Packs);
                Assert.False(result.Success);
                Assert.Contains("newer", result.Error);
                Assert.Equal("{ \"keep\": true }", File.ReadAllText(m.Config));
            }
        }

        [Theory]
        [InlineData("{\"format\":\"other\",\"version\":1}")]
        [InlineData("{\"format\":\"cairo-plus-settings\",\"version\":0}")]
        [InlineData("not json")]
        [InlineData("null")]
        public void Refuses_a_bad_manifest(string manifest)
        {
            using (var m = new Machine())
            {
                string zip = MakeZip(m.Dir, ("manifest.json", manifest), ("cairo-plus.json", "{ }"));
                Assert.False(SettingsBundle.Analyze(zip).Success);
            }
        }

        [Fact]
        public void Refuses_a_theme_zip_and_other_non_bundles()
        {
            using (var m = new Machine())
            {
                string themeZip = MakeZip(m.Dir, ("Mine/theme.json", PackJson));
                Assert.False(SettingsBundle.Analyze(themeZip).Success);

                string notZip = m.Dir.Write("x.zip", "not a zip");
                Assert.False(SettingsBundle.Analyze(notZip).Success);
                Assert.False(SettingsBundle.Analyze(System.IO.Path.Combine(m.Dir.Path, "missing.zip")).Success);
            }
        }

        [Fact]
        public void Refuses_missing_config_and_syntax_errors_without_touching_the_current_one()
        {
            using (var m = new Machine("{ \"enabled\": true }"))
            {
                string noConfig = MakeZip(m.Dir, ("manifest.json", ManifestJson));
                string broken = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ \"enabled\": "));
                Assert.False(SettingsBundle.Import(noConfig, m.Config, m.Packs).Success);
                var result = SettingsBundle.Import(broken, m.Config, m.Packs);
                Assert.False(result.Success);
                Assert.Contains("syntax error", result.Error);
                Assert.Equal("{ \"enabled\": true }", File.ReadAllText(m.Config));
            }
        }

        [Fact]
        public void Refuses_bad_pack_names_and_packs_without_a_manifest()
        {
            using (var m = new Machine())
            {
                string badName = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ }"), ("ThemePacks/.hidden/theme.json", PackJson));
                string noManifest = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ }"), ("ThemePacks/Mine/readme.txt", "x"));
                Assert.False(SettingsBundle.Analyze(badName).Success);
                Assert.Contains("theme.json", SettingsBundle.Analyze(noManifest).Error);
            }
        }

        [Fact]
        public void A_failed_import_leaves_no_staging_folder()
        {
            using (var m = new Machine())
            {
                // The second pack's theme.json is missing, so Analyze fails before extraction; the folder must stay clean.
                string zip = MakeZip(m.Dir, ("manifest.json", ManifestJson), ("cairo-plus.json", "{ }"), ("ThemePacks/A/theme.json", PackJson), ("ThemePacks/B/x.txt", "x"));
                Assert.False(SettingsBundle.Import(zip, m.Config, m.Packs).Success);
                Assert.False(Directory.Exists(m.Packs));
            }
        }
    }
}
