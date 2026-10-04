using System.Collections.Generic;
using System.Windows.Media;
using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class PaletteExpanderTests
    {
        [Theory]
        [InlineData("#F00", 255, 255, 0, 0)]
        [InlineData("#2E3440", 255, 0x2E, 0x34, 0x40)]
        [InlineData("80FFFFFF", 0x80, 255, 255, 255)]
        [InlineData("  #00ff00 ", 255, 0, 255, 0)]
        public void Parses_hex_colours(string text, byte a, byte r, byte g, byte b)
        {
            Assert.True(ColorParser.TryParse(text, out Color c));
            Assert.Equal(Color.FromArgb(a, r, g, b), c);
        }

        [Theory]
        [InlineData("")]
        [InlineData("#12")]
        [InlineData("#GGGGGG")]
        [InlineData("blue")]
        [InlineData("#1234567")]
        public void Rejects_invalid_colours(string text)
        {
            Assert.False(ColorParser.TryParse(text, out _));
        }

        [Fact]
        public void Explicit_roles_map_to_cairo_keys()
        {
            var result = PaletteExpander.Expand(new Dictionary<string, string>
            {
                ["background"] = "#2E3440",
                ["text"] = "#ECEFF4",
                ["accent"] = "#88C0D0",
                ["border"] = "#4C566A"
            });

            Assert.Empty(result.Warnings);
            Assert.Equal(ColorParser.Parse("#2E3440"), result.Brushes["MenuBarBackground"]);
            Assert.Equal(ColorParser.Parse("#2E3440"), result.Brushes["TaskbarBottomBackground"]);
            Assert.Equal(ColorParser.Parse("#ECEFF4"), result.Brushes["MenuItemForeground"]);
            Assert.Equal(ColorParser.Parse("#ECEFF4"), result.Brushes["TaskbarItemForeground"]);
            Assert.Equal(ColorParser.Parse("#88C0D0"), result.Brushes["MenuItemHoverGradient"]);
            Assert.Equal(ColorParser.Parse("#4C566A"), result.Brushes["MenuBarBorder"]);
        }

        [Fact]
        public void Derives_missing_roles_from_background()
        {
            var dark = PaletteExpander.Expand(new Dictionary<string, string> { ["background"] = "#101010" });
            var light = PaletteExpander.Expand(new Dictionary<string, string> { ["background"] = "#F5F5F5" });

            Assert.Equal(Colors.White, dark.Roles["text"]);
            Assert.Equal(Colors.Black, light.Roles["text"]);

            // Every role is resolved even when only background is given.
            foreach (string role in PaletteExpander.RoleNames)
            {
                Assert.True(dark.Roles.ContainsKey(role), role);
            }

            // Surface sits between background and text.
            Assert.True(dark.Roles["surface"].R > 0x10 && dark.Roles["surface"].R < 0xFF);
        }

        [Fact]
        public void Empty_or_null_palette_still_produces_a_complete_brush_set()
        {
            var fromNull = PaletteExpander.Expand(null);
            var fromEmpty = PaletteExpander.Expand(new Dictionary<string, string>());

            Assert.True(fromNull.Brushes.Count > 50);
            Assert.Equal(fromNull.Brushes.Count, fromEmpty.Brushes.Count);
        }

        [Fact]
        public void Role_names_are_case_insensitive()
        {
            var result = PaletteExpander.Expand(new Dictionary<string, string> { ["Accent"] = "#FF0000", ["ACCENTTEXT"] = "#00FF00" });

            Assert.Empty(result.Warnings);
            Assert.Equal(Colors.Red, result.Roles["accent"]);
            Assert.Equal(Color.FromRgb(0, 255, 0), result.Roles["accentText"]);
        }

        [Fact]
        public void Unknown_roles_and_bad_colours_produce_warnings_not_exceptions()
        {
            var result = PaletteExpander.Expand(new Dictionary<string, string>
            {
                ["backgroud"] = "#000000",
                ["accent"] = "not-a-colour"
            });

            Assert.Equal(2, result.Warnings.Count);
            Assert.Contains(result.Warnings, w => w.Contains("backgroud"));
            Assert.Contains(result.Warnings, w => w.Contains("not-a-colour"));
            // Falls back to the default accent.
            Assert.Equal(Color.FromRgb(0x3B, 0x82, 0xF6), result.Roles["accent"]);
        }

        [Fact]
        public void Translucent_states_keep_the_accent_hue()
        {
            var result = PaletteExpander.Expand(new Dictionary<string, string> { ["accent"] = "#88C0D0" });
            Color active = result.Brushes["TaskbarItemActiveBackground"];

            Assert.True(active.A < 255);
            Assert.Equal(0x88, active.R);
            Assert.Equal(0xC0, active.G);
            Assert.Equal(0xD0, active.B);
        }
    }
}
