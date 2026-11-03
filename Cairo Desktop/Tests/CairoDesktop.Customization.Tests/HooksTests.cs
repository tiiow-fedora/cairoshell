using System.Linq;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class HooksTests
    {
        private static ThemePack PackWith(string manifestJson, string folder = "/packs/Demo")
        {
            return new ThemePack("Demo", folder, ConfigLoader.Deserialize<ThemePackManifest>(manifestJson), null);
        }

        private const string PackJson = @"{
  ""name"": ""Demo Theme"",
  ""darkMode"": true,
  ""palette"": { ""background"": ""#101820"", ""text"": ""#EEEEEE"", ""accent"": ""#FF8800"" },
  ""font"": { ""family"": ""Fira Code"" },
  ""hooks"": { ""onThemeApplied"": [ { ""action"": ""run"", ""arg"": ""echo pack"" } ] }
}";

        [Fact]
        public void Parses_hooks_from_config()
        {
            var result = ConfigLoader.Parse(@"{
  ""enabled"": true,
  ""allowPackHooks"": true,
  ""hooks"": {
    // comment
    ""onStart"": [ { ""action"": ""launch"", ""arg"": ""notepad.exe"", ""args"": ""a.txt"" } ],
    ""onThemeApplied"": [ { ""action"": ""run"", ""arg"": ""echo hi"" } ],
  }
}");
            Assert.True(result.Success);
            Assert.True(result.Config.AllowPackHooks);
            Assert.Equal(1, result.Config.Hooks.Get("onStart").Count);
            Assert.Equal("a.txt", result.Config.Hooks.Get("ONSTART")[0].Args);
            Assert.Equal(0, result.Config.Hooks.Get("onConfigReloaded").Count);
            Assert.Equal(0, result.Config.Hooks.Get("nonsense").Count);
            Assert.Equal(2, result.Config.Hooks.Count);
        }

        [Fact]
        public void Pack_hooks_are_blocked_unless_allowed()
        {
            var pack = PackWith(PackJson);

            var blocked = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Theme = "Demo" }, pack);
            Assert.True(blocked.PackHooksBlocked);
            Assert.True(blocked.Hooks.IsEmpty);

            var allowed = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Theme = "Demo", AllowPackHooks = true }, pack);
            Assert.False(allowed.PackHooksBlocked);
            Assert.Equal(1, allowed.Hooks.Get("onThemeApplied").Count);
        }

        [Fact]
        public void User_hooks_run_after_pack_hooks()
        {
            var config = new CairoPlusConfig
            {
                Enabled = true,
                AllowPackHooks = true,
                Hooks = new HooksConfig { OnThemeApplied = new System.Collections.Generic.List<HookAction> { new HookAction { Action = "run", Arg = "echo user" } } }
            };

            var resolved = ResolvedSettings.Resolve(config, PackWith(PackJson));
            var list = resolved.Hooks.Get("onThemeApplied");
            Assert.Equal(2, list.Count);
            Assert.Equal("echo pack", list[0].Arg);
            Assert.Equal("echo user", list[1].Arg);
        }

        [Fact]
        public void User_hooks_work_without_a_pack_and_none_when_disabled()
        {
            var hooks = new HooksConfig { OnStart = new System.Collections.Generic.List<HookAction> { new HookAction { Action = "run", Arg = "echo x" } } };
            Assert.Equal(1, ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Hooks = hooks }, null).Hooks.Count);
            Assert.True(ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = false, Hooks = hooks }, null).Hooks.IsEmpty);
        }

        [Theory]
        [InlineData("run", "echo hi", true)]
        [InlineData("launch", "notepad.exe", true)]
        [InlineData("command", "ToggleDesktopOverlay", true)]
        [InlineData("RUN", "echo hi", true)]
        [InlineData("theme", "next", false)]
        [InlineData("reload", "x", false)]
        [InlineData("run", "", false)]
        [InlineData("fly", "x", false)]
        [InlineData(null, "x", false)]
        public void Validates_hook_actions(string action, string arg, bool ok)
        {
            Assert.Equal(ok, HookAction.Validate(new HookAction { Action = action, Arg = arg }) == null);
        }

        [Fact]
        public void Null_hook_entry_is_invalid()
        {
            Assert.NotNull(HookAction.Validate(null));
        }

        [Fact]
        public void Environment_describes_the_theme()
        {
            var pack = PackWith(PackJson);
            var settings = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Theme = "Demo" }, pack);
            var env = HookEnvironment.Build("onThemeApplied", settings);

            Assert.Equal("onThemeApplied", env["CAIRO_HOOK"]);
            Assert.Equal("Demo", env["CAIRO_THEME"]);
            Assert.Equal("Demo Theme", env["CAIRO_THEME_NAME"]);
            Assert.Equal("1", env["CAIRO_THEME_DARK"]);
            Assert.Equal("Fira Code", env["CAIRO_FONT"]);
            Assert.Equal("#101820", env["CAIRO_COLOR_BACKGROUND"]);
            Assert.Equal("#FF8800", env["CAIRO_COLOR_ACCENT"]);
            Assert.Equal("#EEEEEE", env["CAIRO_COLOR_TEXT"]);
            Assert.True(env.ContainsKey("CAIRO_COLOR_ACCENTTEXT"));
            Assert.True(env.ContainsKey("CAIRO_COLOR_URGENT"));
            Assert.Equal("", env["CAIRO_WALLPAPER"]);
            Assert.True(env["CAIRO_THEME_DIR"].EndsWith("Demo"));
        }

        [Fact]
        public void Environment_dark_flag_falls_back_to_the_background()
        {
            var light = PackWith(@"{ ""palette"": { ""background"": ""#F0F0F0"" } }");
            var env = HookEnvironment.Build("onStart", ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true }, light));
            Assert.Equal("0", env["CAIRO_THEME_DARK"]);
        }

        [Fact]
        public void Environment_without_a_pack_has_empty_theme_values()
        {
            var env = HookEnvironment.Build("onStart", ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true }, null));
            Assert.Equal("", env["CAIRO_THEME"]);
            Assert.Equal("onStart", env["CAIRO_HOOK"]);
            Assert.False(env.Keys.Any(k => k.StartsWith("CAIRO_COLOR_")));
        }
    }
}
