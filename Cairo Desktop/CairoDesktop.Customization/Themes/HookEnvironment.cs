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
    ///   CAIRO_APPEARANCE        light or dark: the variant in effect (empty when Cairo Plus is off)
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
                ["CAIRO_APPEARANCE"] = settings != null && settings.Enabled ? (settings.UsesLightAppearance ? "light" : "dark") : "",
                ["CAIRO_WALLPAPER"] = "",
                ["CAIRO_FONT"] = ""
            };

            var pack = settings?.Pack;
            if (pack?.Manifest == null)
            {
                return env;
            }

            var manifest = pack.Manifest;
            // Pack text is untrusted and "run" hooks go through cmd.exe, which expands %CAIRO_*% before it parses
            // "&", "|" and friends. Free text is cleaned; paths that contain such characters are left out.
            env["CAIRO_THEME"] = CleanText(pack.Id);
            env["CAIRO_THEME_NAME"] = CleanText(pack.DisplayName);
            env["CAIRO_THEME_DIR"] = SafePath(System.IO.Path.GetFullPath(pack.Folder));
            env["CAIRO_WALLPAPER"] = SafePath(settings.GetWallpaperPath() ?? "");
            env["CAIRO_FONT"] = CleanText(manifest.Font?.Family ?? "");

            var expansion = PaletteExpander.Expand(manifest.Palette);
            foreach (var role in expansion.Roles)
            {
                env["CAIRO_COLOR_" + role.Key.ToUpper(CultureInfo.InvariantCulture)] = ColorParser.ToRgbHex(role.Value);
            }

            bool dark = manifest.DarkMode ?? ColorParser.IsDark(expansion.Roles["background"]);
            env["CAIRO_THEME_DARK"] = dark ? "1" : "0";
            return env;
        }

        private static readonly char[] ShellMetaCharacters = { '&', '|', '<', '>', '^', '"', '%', '\r', '\n', '\0' };

        /// <summary>Replaces characters that cmd.exe or an argument list would treat specially with "_".</summary>
        internal static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(ShellMetaCharacters, chars[i]) >= 0 || char.IsControl(chars[i])) chars[i] = '_';
            }

            return new string(chars);
        }

        /// <summary>The path, or "" when it holds a character that could break out of a command line.</summary>
        internal static string SafePath(string path)
        {
            return string.IsNullOrEmpty(path) || path.IndexOfAny(ShellMetaCharacters) >= 0 ? "" : path;
        }
    }
}
