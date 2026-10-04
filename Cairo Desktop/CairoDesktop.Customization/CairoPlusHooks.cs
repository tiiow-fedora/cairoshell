using System;
using System.Collections.Generic;
using System.Windows;
using CairoDesktop.Common;
using CairoDesktop.Customization.Bars;
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

        #region Bars
        private static readonly List<BarHost> _bars = new List<BarHost>();

        internal static IReadOnlyList<BarHost> Bars => _bars;

        internal static event Action<BarHost> BarRegistered;
        internal static event Action<BarHost> BarUnregistered;
        internal static event Action<Window, bool> BarItemsChanging;
        internal static Func<Window, double?> ReservedWidthProvider;

        /// <summary>Called by a bar window once its items exist (after OnSourceInitialized).</summary>
        public static void RegisterBar(BarHost host)
        {
            if (host == null || _bars.Contains(host)) return;
            _bars.Add(host);
            BarRegistered?.Invoke(host);
        }

        /// <summary>Called by a bar window when it closes.</summary>
        public static void UnregisterBar(Window window)
        {
            var host = _bars.Find(b => b.Window == window);
            if (host == null) return;
            _bars.Remove(host);
            BarUnregistered?.Invoke(host);
        }

        /// <summary>
        /// Called before a bar rebuilds its own items (e.g. menu extras toggled in settings): the stock layout
        /// is restored so Cairo's code finds everything where it expects it.
        /// </summary>
        public static void BeginBarItemsChange(Window window) => BarItemsChanging?.Invoke(window, true);

        /// <summary>Called after a bar rebuilt its own items; the custom layout is re-applied.</summary>
        public static void EndBarItemsChange(Window window) => BarItemsChanging?.Invoke(window, false);

        /// <summary>
        /// Width used by everything on the taskbar except its task buttons when the layout is customized,
        /// or null to let Cairo compute it as usual.
        /// </summary>
        public static double? GetTaskbarReservedWidth(Window window) => ReservedWidthProvider?.Invoke(window);
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
