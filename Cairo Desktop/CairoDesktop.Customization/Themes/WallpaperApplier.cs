using System;
using System.IO;
using System.Runtime.InteropServices;
using CairoDesktop.Common;
using ManagedShell.Common.Helpers;
using Microsoft.Win32;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// Applies a theme pack's wallpaper.
    /// As the shell, Cairo draws the desktop itself, so the wallpaper is handed to Cairo's desktop
    /// in memory only (settings.json is never touched). On top of Explorer, Cairo's desktop is
    /// transparent, so the Windows wallpaper is set instead (permanently, by user decision).
    /// </summary>
    public static class WallpaperApplier
    {
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);

        /// <summary>Maps a style name to CairoDesktop.Common.CairoWallpaperStyle.</summary>
        public static CairoWallpaperStyle ToCairoStyle(string style)
        {
            switch ((style ?? "fill").Trim().ToLowerInvariant())
            {
                case "tile": return CairoWallpaperStyle.Tile;
                case "center": return CairoWallpaperStyle.Center;
                case "fit": return CairoWallpaperStyle.Fit;
                case "stretch": return CairoWallpaperStyle.Stretch;
                case "span": return CairoWallpaperStyle.Span;
                default: return CairoWallpaperStyle.Fill;
            }
        }

        /// <returns>An error message, or null.</returns>
        public static string Apply(string path, string style)
        {
            if (path == null)
            {
                CairoPlusHooks.SetWallpaperOverride(null, CairoWallpaperStyle.Fill);
                return null;
            }

            if (!File.Exists(path))
            {
                CairoPlusHooks.SetWallpaperOverride(null, CairoWallpaperStyle.Fill);
                return $"wallpaper '{path}' was not found.";
            }

            if (EnvironmentHelper.IsAppRunningAsShell)
            {
                CairoPlusHooks.SetWallpaperOverride(path, ToCairoStyle(style));
                return null;
            }

            return SetWindowsWallpaper(path, style);
        }

        private static string SetWindowsWallpaper(string path, string style)
        {
            GetRegistryStyle(style, out string wallpaperStyle, out string tile);

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (key == null)
                    {
                        return "could not open the desktop wallpaper settings.";
                    }

                    // Skip the (slow, flickery) system call if nothing would change.
                    if (string.Equals(key.GetValue("Wallpaper") as string, path, StringComparison.OrdinalIgnoreCase) &&
                        key.GetValue("WallpaperStyle") as string == wallpaperStyle &&
                        key.GetValue("TileWallpaper") as string == tile)
                    {
                        return null;
                    }

                    key.SetValue("WallpaperStyle", wallpaperStyle);
                    key.SetValue("TileWallpaper", tile);
                }

                if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE))
                {
                    return $"Windows refused the wallpaper (error {Marshal.GetLastWin32Error()}).";
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void GetRegistryStyle(string style, out string wallpaperStyle, out string tile)
        {
            tile = "0";
            switch ((style ?? "fill").Trim().ToLowerInvariant())
            {
                case "center": wallpaperStyle = "0"; break;
                case "tile": wallpaperStyle = "0"; tile = "1"; break;
                case "stretch": wallpaperStyle = "2"; break;
                case "fit": wallpaperStyle = "6"; break;
                case "span": wallpaperStyle = "22"; break;
                default: wallpaperStyle = "10"; break;
            }
        }
    }
}
