using System.Collections.Generic;
using System.Text.Json;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>theme.json inside a theme pack folder.</summary>
    public sealed class ThemePackManifest
    {
        public string Name { get; set; }

        public string Author { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// Cairo XAML theme loaded underneath this pack ("Default", "Flat", "White", ...).
        /// Defaults to "Default", so the user's own XAML theme doesn't bleed through.
        /// </summary>
        public string BaseTheme { get; set; }

        public bool? DarkMode { get; set; }

        /// <summary>Semantic colours expanded into Cairo's resource keys. See <see cref="PaletteExpander"/>.</summary>
        public Dictionary<string, string> Palette { get; set; }

        /// <summary>Raw overrides by Cairo resource key, applied after the palette.</summary>
        public Dictionary<string, string> Resources { get; set; }

        public FontConfig Font { get; set; }

        public WallpaperConfig Wallpaper { get; set; }

        /// <summary>Cairo UI icon overrides by resource key (e.g. "MenuIcon"), relative to the pack folder.</summary>
        public Dictionary<string, string> Icons { get; set; }

        /// <summary>Per-app icon overrides by exe name, relative to the pack folder.</summary>
        public Dictionary<string, string> AppIcons { get; set; }

        /// <summary>Optional extra ResourceDictionary (relative path) merged last, for full control.</summary>
        public string Xaml { get; set; }

        public BarConfig MenuBar { get; set; }

        public BarConfig Taskbar { get; set; }

        public AnimationConfig Animations { get; set; }

        public Dictionary<string, JsonElement> Widgets { get; set; }
    }

    public sealed class FontConfig
    {
        /// <summary>Font family name. If <see cref="File"/> is set, the family is loaded from that file.</summary>
        public string Family { get; set; }

        /// <summary>Optional .ttf/.otf path relative to the pack folder. Nothing is installed system-wide.</summary>
        public string File { get; set; }

        /// <summary>Base font size (Cairo's "MediumFontSize", default 12). Other sizes scale with it.</summary>
        public double? Size { get; set; }
    }

    public sealed class WallpaperConfig
    {
        /// <summary>Image path relative to the pack folder.</summary>
        public string File { get; set; }

        /// <summary>fill, fit, stretch, center, tile or span. Default fill.</summary>
        public string Style { get; set; }
    }
}
