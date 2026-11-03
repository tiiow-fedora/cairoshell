using System;
using System.Collections.Generic;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// A day or night version of a theme pack. Listed under "light" or "dark" in theme.json. Whatever it sets
    /// replaces the pack's own value; palette roles and resource keys are merged one by one.
    /// </summary>
    public sealed class ThemeVariant
    {
        /// <summary>Palette roles that replace or add to the pack's "palette".</summary>
        public Dictionary<string, string> Palette { get; set; }

        /// <summary>Resource keys that replace or add to the pack's "resources".</summary>
        public Dictionary<string, string> Resources { get; set; }

        /// <summary>Overrides the pack's "darkMode". When left out, the "light" variant means false and "dark" means true.</summary>
        public bool? DarkMode { get; set; }

        /// <summary>Replaces the pack's wallpaper.</summary>
        public WallpaperConfig Wallpaper { get; set; }

        public bool IsEmpty =>
            (Palette == null || Palette.Count == 0) && (Resources == null || Resources.Count == 0) && !DarkMode.HasValue && Wallpaper == null;
    }

    /// <summary>The "appearance" setting in cairo-plus.json.</summary>
    public enum AppearanceMode
    {
        /// <summary>Follow the Windows app mode (light or dark).</summary>
        Auto,
        Light,
        Dark
    }

    /// <summary>Chooses and applies a theme pack's light or dark variant. No UI types, so it is fully testable.</summary>
    public static class ThemeAppearance
    {
        public static readonly string[] ModeNames = { "auto", "light", "dark" };

        /// <summary>Parses "auto", "light" or "dark" (any case). Null or empty means auto; anything else is reported as unknown.</summary>
        public static bool TryParseMode(string text, out AppearanceMode mode)
        {
            mode = AppearanceMode.Auto;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "auto":
                    return true;
                case "light":
                    mode = AppearanceMode.Light;
                    return true;
                case "dark":
                    mode = AppearanceMode.Dark;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether the light variant is in effect. An unknown mode is treated as auto.</summary>
        public static bool IsLight(string modeText, bool windowsUsesLightMode)
        {
            TryParseMode(modeText, out AppearanceMode mode);
            switch (mode)
            {
                case AppearanceMode.Light: return true;
                case AppearanceMode.Dark: return false;
                default: return windowsUsesLightMode;
            }
        }

        public static bool HasVariants(ThemePackManifest manifest)
        {
            return manifest != null && ((manifest.Light != null && !manifest.Light.IsEmpty) || (manifest.Dark != null && !manifest.Dark.IsEmpty));
        }

        /// <summary>
        /// The manifest as it applies to the light or dark appearance. A pack without a variant for that appearance
        /// is returned unchanged. The original manifest is never modified.
        /// </summary>
        public static ThemePackManifest Apply(ThemePackManifest manifest, bool light)
        {
            if (manifest == null) return null;

            ThemeVariant variant = light ? manifest.Light : manifest.Dark;
            if (variant == null || variant.IsEmpty) return manifest;

            return new ThemePackManifest
            {
                Name = manifest.Name,
                Author = manifest.Author,
                Description = manifest.Description,
                BaseTheme = manifest.BaseTheme,
                DarkMode = variant.DarkMode ?? !light,
                Palette = Merge(manifest.Palette, variant.Palette),
                Resources = Merge(manifest.Resources, variant.Resources),
                Font = manifest.Font,
                Wallpaper = variant.Wallpaper ?? manifest.Wallpaper,
                Icons = manifest.Icons,
                AppIcons = manifest.AppIcons,
                Xaml = manifest.Xaml,
                MenuBar = manifest.MenuBar,
                Taskbar = manifest.Taskbar,
                Animations = manifest.Animations,
                Widgets = manifest.Widgets,
                Hooks = manifest.Hooks,
                Light = manifest.Light,
                Dark = manifest.Dark
            };
        }

        /// <summary>The same pack with its manifest replaced by the variant that applies; the pack itself if nothing changes.</summary>
        public static ThemePack Apply(ThemePack pack, bool light)
        {
            if (pack?.Manifest == null) return pack;
            var effective = Apply(pack.Manifest, light);
            return ReferenceEquals(effective, pack.Manifest) ? pack : pack.WithManifest(effective);
        }

        private static Dictionary<string, string> Merge(Dictionary<string, string> under, Dictionary<string, string> over)
        {
            if (over == null || over.Count == 0) return under;

            // Case-insensitive so "Accent" in a variant replaces "accent" in the pack.
            var merged = new Dictionary<string, string>(under ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in over) merged[entry.Key] = entry.Value;
            return merged;
        }
    }
}
