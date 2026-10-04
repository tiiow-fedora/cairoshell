using System;
using System.IO;
using System.Reflection;

namespace CairoDesktop.Customization
{
    public static class CairoPlusPaths
    {
        public const string ConfigFileName = "cairo-plus.json";
        public const string ThemePacksFolderName = "ThemePacks";
        public const string WidgetsFolderName = "Widgets";
        public const string ManifestFileName = "theme.json";

        /// <summary>%LOCALAPPDATA%\Cairo Desktop (shared with stock Cairo; we only add our own files).</summary>
        public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cairo Desktop");

        public static string AppFolder => Path.GetDirectoryName((Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).Location);

        public static string ConfigFile => Path.Combine(DataFolder, ConfigFileName);

        public static string UserThemePacksFolder => Path.Combine(DataFolder, ThemePacksFolderName);

        public static string UserWidgetsFolder => Path.Combine(DataFolder, WidgetsFolderName);

        /// <summary>Theme pack folders, lowest priority first. A user pack with the same name wins over a bundled one.</summary>
        public static string[] ThemePackFolders => new[]
        {
            Path.Combine(AppFolder, ThemePacksFolderName),
            UserThemePacksFolder
        };

        public static string[] WidgetFolders => new[]
        {
            Path.Combine(AppFolder, WidgetsFolderName),
            UserWidgetsFolder
        };
    }
}
