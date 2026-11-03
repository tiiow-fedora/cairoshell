using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CairoDesktop.Customization.Cli;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class ThemeNamesTests
    {
        [Theory]
        [InlineData("Nord", true)]
        [InlineData("Tokyo Night", true)]
        [InlineData("rose-pine_2", true)]
        [InlineData("", false)]
        [InlineData("  ", false)]
        [InlineData(" lead", false)]
        [InlineData(".hidden", false)]
        [InlineData("a/b", false)]
        [InlineData("a\\b", false)]
        [InlineData("..", false)]
        [InlineData("con:", false)]
        public void Validates_names(string name, bool ok)
        {
            Assert.Equal(ok, ThemeNames.Validate(name) == null);
        }

        [Theory]
        [InlineData("tokyo night", "TokyoNight")]
        [InlineData("My   Theme!", "MyTheme")]
        [InlineData("rose-pine", "Rose-pine")]
        [InlineData("Solarized Dark", "SolarizedDark")]
        public void Makes_folder_names(string display, string expected)
        {
            Assert.Equal(expected, ThemeNames.ToFolderName(display));
        }

        [Fact]
        public void Unique_names_get_a_suffix()
        {
            using (var dir = new TempDir())
            {
                Directory.CreateDirectory(Path.Combine(dir.Path, "Nord"));
                Directory.CreateDirectory(Path.Combine(dir.Path, "Nord-2"));
                Assert.Equal("Nord-3", ThemeNames.MakeUnique(dir.Path, "Nord"));
                Assert.Equal("Other", ThemeNames.MakeUnique(dir.Path, "Other"));
            }
        }
    }

    public class ThemeScaffoldTests
    {
        [Fact]
        public void Template_is_a_valid_clean_pack()
        {
            using (var dir = new TempDir())
            {
                var result = ThemeScaffold.Create(dir.Path, "My Theme");
                Assert.True(result.Success);
                Assert.Equal("MyTheme", Path.GetFileName(result.Folder));

                var pack = ThemePackLoader.LoadFolder(result.Folder);
                Assert.Null(pack.Error);
                Assert.Equal("My Theme", pack.Manifest.Name);
                Assert.True(pack.Manifest.DarkMode);
                Assert.Empty(ThemeValidator.Validate(pack).Where(i => i.Severity != IssueSeverity.Info));
            }
        }

        [Fact]
        public void Template_survives_quotes_in_the_name()
        {
            using (var dir = new TempDir())
            {
                // Quotes are refused by name validation, so the template can't be broken by them.
                Assert.NotNull(ThemeScaffold.Create(dir.Path, "Bad\"Name").Error);
            }
        }

        [Fact]
        public void Refuses_to_overwrite_and_bad_names()
        {
            using (var dir = new TempDir())
            {
                Assert.True(ThemeScaffold.Create(dir.Path, "Dup").Success);
                Assert.Contains("already exists", ThemeScaffold.Create(dir.Path, "Dup").Error);
                Assert.NotNull(ThemeScaffold.Create(dir.Path, "../escape").Error);
                Assert.NotNull(ThemeScaffold.Create(dir.Path, "").Error);
            }
        }

        [Fact]
        public void Copy_renames_and_keeps_files()
        {
            using (var dir = new TempDir())
            {
                dir.Write("src/Orig/theme.json", "// c\n{ \"name\": \"Orig\", \"palette\": { \"background\": \"#111111\" } }");
                dir.Write("src/Orig/icons/MenuIcon.png", "png");

                var source = ThemePackLoader.LoadFolder(Path.Combine(dir.Path, "src", "Orig"));
                var result = ThemeScaffold.Create(Path.Combine(dir.Path, "user"), "Copy Cat", source);

                Assert.True(result.Success);
                Assert.True(File.Exists(Path.Combine(result.Folder, "icons", "MenuIcon.png")));
                var copy = ThemePackLoader.LoadFolder(result.Folder);
                Assert.Equal("Copy Cat", copy.Manifest.Name);
                Assert.Equal("#111111", copy.Manifest.Palette["background"]);
                Assert.Contains("// c", File.ReadAllText(Path.Combine(result.Folder, "theme.json")));
                Assert.Equal("Orig", ThemePackLoader.LoadFolder(source.Folder).Manifest.Name);
            }
        }
    }

    public class ThemeArchiveTests
    {
        private static void MakeZip(string path, params (string name, string content)[] entries)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    var e = zip.CreateEntry(entry.name);
                    using (var writer = new StreamWriter(e.Open(), new UTF8Encoding(false))) writer.Write(entry.content);
                }
            }
        }

        [Fact]
        public void Export_then_install_round_trips()
        {
            using (var dir = new TempDir())
            {
                dir.Write("src/Demo/theme.json", "{ \"name\": \"Demo\", \"palette\": { \"background\": \"#222222\" } }");
                dir.Write("src/Demo/icons/MenuIcon.png", "png");
                var pack = ThemePackLoader.LoadFolder(Path.Combine(dir.Path, "src", "Demo"));

                string zip = Path.Combine(dir.Path, "out", "Demo.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(zip));
                Assert.True(ThemeArchive.Export(pack, zip).Success);

                var installed = ThemeArchive.Install(zip, Path.Combine(dir.Path, "user"));
                Assert.True(installed.Success);
                Assert.Equal("Demo", installed.PackId);
                Assert.True(File.Exists(Path.Combine(installed.Path, "icons", "MenuIcon.png")));
                Assert.Null(ThemePackLoader.LoadFolder(installed.Path).Error);

                // A second install never replaces the first.
                var again = ThemeArchive.Install(zip, Path.Combine(dir.Path, "user"));
                Assert.Equal("Demo-2", again.PackId);
                Assert.Empty(Directory.GetDirectories(Path.Combine(dir.Path, "user")).Where(d => Path.GetFileName(d).StartsWith(".")));
            }
        }

        [Fact]
        public void Installs_zip_with_manifest_at_the_root_using_the_manifest_name()
        {
            using (var dir = new TempDir())
            {
                string zip = Path.Combine(dir.Path, "whatever.zip");
                MakeZip(zip, ("theme.json", "{ \"name\": \"Root Pack\" }"), ("wallpaper.jpg", "x"));
                var result = ThemeArchive.Install(zip, Path.Combine(dir.Path, "user"));
                Assert.True(result.Success);
                Assert.Equal("RootPack", result.PackId);
                Assert.True(File.Exists(Path.Combine(result.Path, "wallpaper.jpg")));
            }
        }

        [Theory]
        [InlineData("Pack/../../evil.txt")]
        [InlineData("../evil.txt")]
        [InlineData("/etc/evil.txt")]
        [InlineData("C:/evil.txt")]
        public void Refuses_path_traversal(string badName)
        {
            using (var dir = new TempDir())
            {
                string zip = Path.Combine(dir.Path, "bad.zip");
                MakeZip(zip, ("Pack/theme.json", "{ }"), (badName, "boom"));
                var result = ThemeArchive.Install(zip, Path.Combine(dir.Path, "user"));
                Assert.False(result.Success);
                Assert.False(File.Exists(Path.Combine(dir.Path, "evil.txt")));
                Assert.False(File.Exists(Path.Combine(dir.Path, "user", "evil.txt")));
            }
        }

        [Fact]
        public void Refuses_archives_without_a_manifest_or_with_several()
        {
            using (var dir = new TempDir())
            {
                string none = Path.Combine(dir.Path, "none.zip");
                MakeZip(none, ("readme.txt", "hi"));
                Assert.Contains("theme.json", ThemeArchive.Install(none, Path.Combine(dir.Path, "user")).Error);

                string many = Path.Combine(dir.Path, "many.zip");
                MakeZip(many, ("A/theme.json", "{}"), ("B/theme.json", "{}"));
                Assert.Contains("more than one", ThemeArchive.Install(many, Path.Combine(dir.Path, "user")).Error);
            }
        }

        [Fact]
        public void Refuses_a_broken_manifest_and_cleans_up()
        {
            using (var dir = new TempDir())
            {
                string zip = Path.Combine(dir.Path, "broken.zip");
                MakeZip(zip, ("Pack/theme.json", "{ \"name\": "));
                string user = Path.Combine(dir.Path, "user");
                Assert.False(ThemeArchive.Install(zip, user).Success);
                Assert.Empty(Directory.GetDirectories(user));
            }
        }

        [Fact]
        public void Refuses_a_file_that_is_not_a_zip()
        {
            using (var dir = new TempDir())
            {
                string path = dir.Write("fake.zip", "not a zip");
                Assert.False(ThemeArchive.Install(path, Path.Combine(dir.Path, "user")).Success);
            }
        }
    }

    public class ThemeCliTests
    {
        private static (int code, string output) Run(ThemeCliContext context, params string[] args)
        {
            var writer = new StringWriter();
            int code = ThemeCli.Run(args, context, writer);
            return (code, writer.ToString());
        }

        private static ThemeCliContext Context(TempDir dir)
        {
            return new ThemeCliContext
            {
                PackFolders = new[] { Path.Combine(dir.Path, "bundled"), Path.Combine(dir.Path, "user") },
                UserPacksFolder = Path.Combine(dir.Path, "user"),
                ConfigFile = Path.Combine(dir.Path, "cairo-plus.json"),
                CurrentDirectory = dir.Path
            };
        }

        [Fact]
        public void Finds_the_command_in_any_switch_style()
        {
            Assert.Equal("list-themes", ThemeCli.FindCommand(new[] { "--list-themes" }));
            Assert.Equal("validate-theme", ThemeCli.FindCommand(new[] { "/validate-theme", "Nord" }));
            Assert.Equal("new-theme", ThemeCli.FindCommand(new[] { "--new-theme=Foo" }));
            Assert.Null(ThemeCli.FindCommand(new[] { "--apply-theme", "Nord" }));
            Assert.Null(ThemeCli.FindCommand(new string[0]));
        }

        [Fact]
        public void Lists_packs_and_marks_the_active_one()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Nord/theme.json", "{ \"name\": \"Nord\", \"description\": \"cool\" }");
                dir.Write("user/Mine/theme.json", "{ \"name\": \"Mine\" }");
                dir.Write("bundled/Broken/theme.json", "{");
                dir.Write("cairo-plus.json", "{ \"enabled\": true, \"theme\": \"Mine\" }");

                var (code, output) = Run(Context(dir), "--list-themes");
                Assert.Equal(0, code);
                Assert.Contains("* Mine", output);
                Assert.Contains("  Nord", output);
                Assert.Contains("bundled", output);
                Assert.Contains("user", output);
                Assert.Contains("ERROR", output);
            }
        }

        [Fact]
        public void Lists_which_packs_follow_the_windows_app_mode()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Pair/theme.json", "{ \"name\": \"Pair\", \"light\": { \"palette\": { \"background\": \"#FFFFFF\" } } }");
                dir.Write("bundled/Plain/theme.json", "{ \"name\": \"Plain\" }");

                string output = Run(Context(dir), "--list-themes").output;
                Assert.Contains("Pair [light/dark]", output);
                Assert.DoesNotContain("Plain [light/dark]", output);
            }
        }

        [Fact]
        public void Active_marker_ignored_when_cairo_plus_is_off()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Nord/theme.json", "{ }");
                dir.Write("cairo-plus.json", "{ \"enabled\": false, \"theme\": \"Nord\" }");
                Assert.DoesNotContain("* Nord", Run(Context(dir), "--list-themes").output);
            }
        }

        [Fact]
        public void Validate_exit_code_follows_errors_not_warnings()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Good/theme.json", "{ \"name\": \"Good\", \"palette\": { \"background\": \"#202020\" } }");
                dir.Write("bundled/Warny/theme.json", "{ \"palette\": { \"background\": \"#202020\", \"text\": \"#252525\" } }");
                dir.Write("bundled/Bad/theme.json", "{ \"wallpaper\": { \"file\": \"nope.jpg\" } }");
                var context = Context(dir);

                Assert.Equal(0, Run(context, "--validate-theme", "Good").code);
                var warny = Run(context, "--validate-theme", "Warny");
                Assert.Equal(0, warny.code);
                Assert.Contains("contrast", warny.output);
                var bad = Run(context, "--validate-theme", "Bad");
                Assert.Equal(1, bad.code);
                Assert.Contains("1 error(s)", bad.output);
                Assert.Equal(1, Run(context, "--validate-theme").code);
                Assert.Equal(2, Run(context, "--validate-theme", "Missing").code);
            }
        }

        [Fact]
        public void Validate_accepts_a_relative_folder()
        {
            using (var dir = new TempDir())
            {
                dir.Write("work/Pack/theme.json", "{ \"name\": \"P\", \"description\": \"d\", \"palette\": { \"background\": \"#202020\" } }");
                var (code, output) = Run(Context(dir), "--validate-theme", "./work/Pack");
                Assert.Equal(0, code);
                Assert.Contains("Pack: ok", output);
            }
        }

        [Fact]
        public void New_theme_creates_a_pack_and_copies_with_from()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Nord/theme.json", "{ \"name\": \"Nord\" }");
                var context = Context(dir);

                Assert.Equal(0, Run(context, "--new-theme", "Fresh One").code);
                Assert.True(File.Exists(Path.Combine(dir.Path, "user", "FreshOne", "theme.json")));

                Assert.Equal(0, Run(context, "--new-theme", "Nordic", "--from", "Nord").code);
                Assert.Equal("Nordic", ThemePackLoader.LoadFolder(Path.Combine(dir.Path, "user", "Nordic")).Manifest.Name);

                Assert.Equal(1, Run(context, "--new-theme", "Fresh One").code);
                Assert.Equal(2, Run(context, "--new-theme", "X", "--from", "Nope").code);
                Assert.Equal(2, Run(context, "--new-theme").code);
            }
        }

        [Fact]
        public void Export_and_install_through_the_cli()
        {
            using (var dir = new TempDir())
            {
                dir.Write("bundled/Nord/theme.json", "{ \"name\": \"Nord\", \"hooks\": { \"onStart\": [ { \"action\": \"run\", \"arg\": \"echo\" } ] } }");
                var context = Context(dir);

                var export = Run(context, "--export-theme", "Nord");
                Assert.Equal(0, export.code);
                Assert.True(File.Exists(Path.Combine(dir.Path, "Nord.zip")));

                var install = Run(context, "--install-theme", "Nord.zip");
                Assert.Equal(0, install.code);
                Assert.Contains("Installed 'Nord'", install.output);
                Assert.Contains("allowPackHooks", install.output);
                Assert.True(File.Exists(Path.Combine(dir.Path, "user", "Nord", "theme.json")));

                Assert.Equal(2, Run(context, "--install-theme", "missing.zip").code);
                Assert.Equal(2, Run(context, "--export-theme", "Nope").code);
            }
        }
    }
}
