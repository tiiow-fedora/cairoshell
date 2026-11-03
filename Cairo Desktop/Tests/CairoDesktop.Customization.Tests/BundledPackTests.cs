using System.Linq;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class BundledPackTests
    {
        [Theory]
        [InlineData("Catppuccin")]
        [InlineData("Ayu")]
        [InlineData("AtomOne")]
        public void Day_night_packs_switch_palette_wallpaper_and_dark_flag(string id)
        {
            var pack = ThemePackLoader.Discover(new[] { TestPaths.BundledPacks }).Single(p => p.Id == id);
            Assert.True(ThemeAppearance.HasVariants(pack.Manifest));

            var config = new CairoPlusConfig { Enabled = true, Theme = id };
            var day = ResolvedSettings.Resolve(config, pack, windowsUsesLightMode: true);
            var night = ResolvedSettings.Resolve(config, pack, windowsUsesLightMode: false);

            Assert.False(day.Pack.Manifest.DarkMode);
            Assert.True(night.Pack.Manifest.DarkMode);

            var dayBackground = PaletteExpander.Expand(day.Pack.Manifest.Palette).Roles["background"];
            var nightBackground = PaletteExpander.Expand(night.Pack.Manifest.Palette).Roles["background"];
            Assert.False(ColorParser.IsDark(dayBackground));
            Assert.True(ColorParser.IsDark(nightBackground));

            Assert.NotEqual(day.GetWallpaperPath(), night.GetWallpaperPath());
            Assert.True(System.IO.File.Exists(day.GetWallpaperPath()));
            Assert.True(System.IO.File.Exists(night.GetWallpaperPath()));
        }

        [Fact]
        public void Every_bundled_pack_has_a_unique_name_and_description()
        {
            var packs = ThemePackLoader.Discover(new[] { TestPaths.BundledPacks });
            Assert.Equal(packs.Count, packs.Select(p => p.DisplayName).Distinct().Count());
            Assert.All(packs, p => Assert.False(string.IsNullOrWhiteSpace(p.Manifest.Description)));
        }
    }
}
