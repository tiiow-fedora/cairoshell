using System;
using System.Collections.Generic;
using System.Globalization;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// The CAIRO_* environment variables a hook's "run" command receives, so a script can restyle other
    /// programs to match the theme:
    ///   CAIRO_HOOK              the hook that fired (onStart, onThemeApplied, onConfigReloaded)
    ///   CAIRO_THEME             pack folder name, empty when no pack is active
    ///   CAIRO_THEME_NAME        the pack's display name
    ///   CAIRO_THEME_DIR         absolute path of the pack folder
    ///   CAIRO_THEME_DARK        1 or 0 (from the pack's darkMode, else from the background colour)
    ///   CAIRO_WALLPAPER         absolute path of the pack's wallpaper, if any
    ///   CAIRO_FONT              the pack's font family, if any
    ///   CAIRO_COLOR_BACKGROUND, CAIRO_COLOR_SURFACE, CAIRO_COLOR_OVERLAY, CAIRO_COLOR_BORDER, CAIRO_COLOR_TEXT,
    ///   CAIRO_COLOR_SUBTEXT, CAIRO_COLOR_ACCENT, CAIRO_COLOR_ACCENTTEXT, CAIRO_COLOR_URGENT   "#RRGGBB"
    /// </summary>
    public static class HookEnvironment
    {
        public static Dictionary<string, string> Build(string hookName, ResolvedSettings settings)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CAIRO_HOOK"] = hookName ?? "",
                ["CAIRO_THEME"] = "",
                ["CAIRO_THEME_NAME"] = "",
                ["CAIRO_THEME_DIR"] = "",
                ["CAIRO_THEME_DARK"] = "",
                ["CAIRO_WALLPAPER"] = "",
                ["CAIRO_FONT"] = ""
            };

            var pack = settings?.Pack;
            if (pack?.Manifest == null)
            {
                return env;
            }

            var manifest = pack.Manifest;
            env["CAIRO_THEME"] = pack.Id;
            env["CAIRO_THEME_NAME"] = pack.DisplayName;
            env["CAIRO_THEME_DIR"] = System.IO.Path.GetFullPath(pack.Folder);
            env["CAIRO_WALLPAPER"] = settings.GetWallpaperPath() ?? "";
            env["CAIRO_FONT"] = manifest.Font?.Family ?? "";

            var expansion = PaletteExpander.Expand(manifest.Palette);
            foreach (var role in expansion.Roles)
            {
                env["CAIRO_COLOR_" + role.Key.ToUpper(CultureInfo.InvariantCulture)] = ColorParser.ToRgbHex(role.Value);
            }

            bool dark = manifest.DarkMode ?? ColorParser.IsDark(expansion.Roles["background"]);
            env["CAIRO_THEME_DARK"] = dark ? "1" : "0";
            return env;
        }
    }
}
