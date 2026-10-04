using System;
using System.Windows;
using CairoDesktop.Common;
using ManagedShell.Common.Logging;

namespace CairoDesktop.Customization
{
    /// <summary>
    /// The only surface upstream Cairo code calls into (search the solution for "CAIRO-PLUS").
    /// Every member is a no-op until <see cref="CairoPlusService"/> is enabled, so stock
    /// behaviour is unchanged when Cairo Plus is off.
    /// </summary>
    public static class CairoPlusHooks
    {
        private static Func<ResourceDictionary, ResourceDictionary> _themeLayerFactory;
        private static string _wallpaperPath;
        private static CairoWallpaperStyle _wallpaperStyle;

        #region Theme
        /// <summary>
        /// XAML theme Cairo should load instead of the one in its settings, while a theme pack is active.
        /// Null means "use Cairo's setting".
        /// </summary>
        public static string ThemeOverride { get; private set; }

        /// <summary>
        /// Called by Cairo's theme service after it has (re)loaded its XAML theme. Adds the theme pack layer on top.
        /// </summary>
        public static void OnThemeApplied(ResourceDictionary applicationResources)
        {
            var factory = _themeLayerFactory;
            if (factory == null || applicationResources == null)
            {
                return;
            }

            try
            {
                var layer = factory(applicationResources);
                if (layer != null)
                {
                    applicationResources.MergedDictionaries.Add(layer);
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Error($"CairoPlus: unable to apply theme pack layer: {ex.Message}");
            }
        }

        internal static void SetTheme(string themeOverride, Func<ResourceDictionary, ResourceDictionary> layerFactory)
        {
            ThemeOverride = themeOverride;
            _themeLayerFactory = layerFactory;
        }
        #endregion

        #region Wallpaper (only used when Cairo is the shell and draws its own desktop)
        /// <summary>Raised when the in-memory wallpaper override changes. Cairo's desktop reloads its background.</summary>
        public static event EventHandler WallpaperChanged;

        public static bool TryGetWallpaperOverride(out string path, out CairoWallpaperStyle style)
        {
            path = _wallpaperPath;
            style = _wallpaperStyle;
            return path != null;
        }

        internal static void SetWallpaperOverride(string path, CairoWallpaperStyle style)
        {
            if (string.Equals(path, _wallpaperPath, StringComparison.OrdinalIgnoreCase) && style == _wallpaperStyle)
            {
                return;
            }

            _wallpaperPath = path;
            _wallpaperStyle = style;
            WallpaperChanged?.Invoke(null, EventArgs.Empty);
        }
        #endregion
    }
}
