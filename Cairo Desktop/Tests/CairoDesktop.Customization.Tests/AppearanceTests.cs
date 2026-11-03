using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class AppearanceTests
    {
        private const string PairJson = @"{
  ""name"": ""Pair"",
  ""darkMode"": true,
  ""palette"": { ""background"": ""#101820"", ""text"": ""#EEEEEE"", ""accent"": ""#FF8800"" },
  ""resources"": { ""MenuTopBorderBrush"": ""#FF8800"" },
  ""wallpaper"": { ""file"": ""night.jpg"" },
  ""light"": {
    ""palette"": { ""background"": ""#F5F5F5"", ""text"": ""#202020"" },
    ""wallpaper"": { ""file"": ""day.jpg"", ""style"": ""fit"" }
  }
}";

        private static ThemePack Pair(string json = PairJson)
        {
            return new ThemePack("Pair", "/packs/Pair", ConfigLoader.Deserialize<ThemePackManifest>(json), null);
        }

        [Theory]
        [InlineData(null, true, true)]
        [InlineData("", false, false)]
        [InlineData("auto", true, true)]
        [InlineData("AUTO", false, false)]
        [InlineData("light", false, true)]
        [InlineData("Light", true, true)]
        [InlineData("dark", true, false)]
        [InlineData("dark", false, false)]
        [InlineData("sepia", true, true)]
        [InlineData("sepia", false, false)]
        public void Mode_picks_light_or_dark(string mode, bool windowsLight, bool expected)
        {
            Assert.Equal(expected, ThemeAppearance.IsLight(mode, windowsLight));
        }

        [Theory]
        [InlineData("auto", true)]
        [InlineData(null, true)]
        [InlineData("light", true)]
        [InlineData("DARK", true)]
        [InlineData("night", false)]
        public void Mode_text_is_parsed(string text, bool valid)
        {
            Assert.Equal(valid, ThemeAppearance.TryParseMode(text, out _));
        }

        [Fact]
        public void Light_variant_overrides_only_what_it_sets()
        {
            var effective = ThemeAppearance.Apply(Pair().Manifest, light: true);

            Assert.Equal("#F5F5F5", effective.Palette["background"]);
            Assert.Equal("#202020", effective.Palette["text"]);
            Assert.Equal("#FF8800", effective.Palette["accent"]);
            Assert.Equal("#FF8800", effective.Resources["MenuTopBorderBrush"]);
            Assert.Equal("day.jpg", effective.Wallpaper.File);
            Assert.Equal("fit", effective.Wallpaper.Style);
            Assert.Equal("Pair", effective.Name);
        }

        [Fact]
        public void Variant_dark_mode_defaults_from_its_name()
        {
            Assert.False(ThemeAppearance.Apply(Pair().Manifest, light: true).DarkMode);
            var lightPack = Pair(@"{ ""darkMode"": false, ""dark"": { ""palette"": { ""background"": ""#000000"" } } }");
            Assert.True(ThemeAppearance.Apply(lightPack.Manifest, light: false).DarkMode);
        }

        [Fact]
        public void Variant_dark_mode_can_be_set_explicitly()
        {
            var pack = Pair(@"{ ""light"": { ""darkMode"": true, ""palette"": { ""background"": ""#000000"" } } }");
            Assert.True(ThemeAppearance.Apply(pack.Manifest, light: true).DarkMode);
        }

        [Fact]
        public void Missing_variant_leaves_the_manifest_alone_and_never_mutates()
        {
            var pack = Pair();
            Assert.Same(pack.Manifest, ThemeAppearance.Apply(pack.Manifest, light: false));
            Assert.Same(pack, ThemeAppearance.Apply(pack, light: false));

            ThemeAppearance.Apply(pack.Manifest, light: true);
            Assert.Equal("#101820", pack.Manifest.Palette["background"]);
            Assert.Equal("night.jpg", pack.Manifest.Wallpaper.File);
        }

        [Fact]
        public void Variant_palette_keys_match_regardless_of_case()
        {
            var pack = Pair(@"{ ""palette"": { ""Accent"": ""#112233"" }, ""light"": { ""palette"": { ""accent"": ""#445566"" } } }");
            var palette = ThemeAppearance.Apply(pack.Manifest, light: true).Palette;
            Assert.Single(palette);
            Assert.Equal("#445566", palette["accent"]);
        }

        [Fact]
        public void Empty_variant_counts_as_missing()
        {
            var pack = Pair(@"{ ""palette"": { ""background"": ""#000000"" }, ""light"": { } }");
            Assert.False(ThemeAppearance.HasVariants(pack.Manifest));
            Assert.Same(pack.Manifest, ThemeAppearance.Apply(pack.Manifest, light: true));
            Assert.True(ThemeAppearance.HasVariants(Pair().Manifest));
        }

        [Fact]
        public void Resolve_uses_windows_mode_when_auto()
        {
            var config = new CairoPlusConfig { Enabled = true, Theme = "Pair" };

            var day = ResolvedSettings.Resolve(config, Pair(), windowsUsesLightMode: true);
            Assert.True(day.UsesLightAppearance);
            Assert.Equal("#F5F5F5", day.Pack.Manifest.Palette["background"]);
            Assert.EndsWith("day.jpg", day.GetWallpaperPath());

            var night = ResolvedSettings.Resolve(config, Pair(), windowsUsesLightMode: false);
            Assert.False(night.UsesLightAppearance);
            Assert.Equal("#101820", night.Pack.Manifest.Palette["background"]);
            Assert.EndsWith("night.jpg", night.GetWallpaperPath());
        }

        [Fact]
        public void Config_appearance_overrides_windows_mode()
        {
            var light = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Appearance = "light" }, Pair(), windowsUsesLightMode: false);
            Assert.Equal("#F5F5F5", light.Pack.Manifest.Palette["background"]);

            var dark = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true, Appearance = "dark" }, Pair(), windowsUsesLightMode: true);
            Assert.Equal("#101820", dark.Pack.Manifest.Palette["background"]);
        }

        [Fact]
        public void Hook_environment_reports_the_variant_in_effect()
        {
            var pack = Pair();
            var day = HookEnvironment.Build("onStart", ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true }, pack, true));
            Assert.Equal("light", day["CAIRO_APPEARANCE"]);
            Assert.Equal("0", day["CAIRO_THEME_DARK"]);
            Assert.Equal("#F5F5F5", day["CAIRO_COLOR_BACKGROUND"]);

            var night = HookEnvironment.Build("onStart", ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = true }, pack, false));
            Assert.Equal("dark", night["CAIRO_APPEARANCE"]);
            Assert.Equal("1", night["CAIRO_THEME_DARK"]);

            Assert.Equal("", HookEnvironment.Build("onStart", ResolvedSettings.Disabled)["CAIRO_APPEARANCE"]);
        }

        [Fact]
        public void Config_parses_appearance_and_flags_unknown_values()
        {
            var ok = ConfigLoader.Parse(@"{ ""enabled"": true, ""appearance"": ""Light"" }");
            Assert.Equal("Light", ok.Config.Appearance);
            Assert.Empty(ConfigChecker.Check(ok.Config));

            var bad = ConfigLoader.Parse(@"{ ""appearance"": ""sepia"" }");
            Assert.Single(ConfigChecker.Check(bad.Config));
        }

        [Fact]
        public void Disabled_config_ignores_appearance()
        {
            var settings = ResolvedSettings.Resolve(new CairoPlusConfig { Enabled = false, Theme = "Pair" }, Pair(), true);
            Assert.Null(settings.Pack);
        }
    }
}
