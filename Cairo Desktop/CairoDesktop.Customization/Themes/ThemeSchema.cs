using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// The vocabulary a theme pack can use: names Cairo Plus recognises. Kept free of any UI types so the
    /// theme validator and its tests run anywhere.
    /// </summary>
    public static partial class ThemeSchema
    {
        /// <summary>Cairo's own UI icons. A pack can override any of these via "icons" or icons/&lt;Key&gt;.png.</summary>
        public static readonly string[] IconKeys =
        {
            "MenuIcon", "SearchIcon", "ActionCenterIcon", "VolumeIcon", "VolumeLowIcon", "VolumeOffIcon",
            "VolumeMuteIcon", "DateTimeIcon", "DesktopOverlayIcon", "TaskListMenuIcon", "TaskViewIcon",
            "DesktopToolbarBackIcon", "DesktopToolbarUpIcon", "DesktopToolbarForwardIcon",
            "DesktopToolbarBrowseIcon", "DesktopToolbarHomeIcon"
        };

        public static readonly string[] WallpaperStyles = { "fill", "fit", "stretch", "center", "tile", "span" };

        /// <summary>XAML themes that ship with Cairo. Users can add more under %LOCALAPPDATA%\Cairo Desktop\Themes.</summary>
        public static readonly string[] BaseThemes = { "Default", "Fade", "Flat", "Flat Accent", "MiniMeow", "White" };

        public static readonly string[] MenuBarItems = { "cairoMenu", "programsMenu", "placesMenu", "stacks", "tray", "volume", "actionCenter", "clock", "search" };

        public static readonly string[] TaskbarItems = { "desktopButton", "quickLaunch", "tasks", "taskList" };

        /// <summary>Widget types built into Cairo Plus. Widget DLLs can add more.</summary>
        public static readonly string[] BuiltInWidgetTypes = { "clock", "cpu", "memory", "battery", "network", "command" };

        public static readonly string[] HookNames = { "onStart", "onThemeApplied", "onConfigReloaded" };

        /// <summary>Actions allowed in hooks. "theme" and "reload" are left out because a hook could re-trigger itself.</summary>
        public static readonly string[] HookActions = { "launch", "run", "command" };

        public static bool IsKnownIcon(string key) => IconKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnownWallpaperStyle(string style) => WallpaperStyles.Contains((style ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
