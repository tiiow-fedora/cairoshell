using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// Builds the ResourceDictionary that a theme pack layers on top of Cairo's XAML theme.
    /// Every Cairo theme key it touches is referenced with DynamicResource in Cairo's XAML,
    /// so swapping this dictionary restyles the running shell in place.
    /// </summary>
    public static class ThemeLayerBuilder
    {
        /// <summary>Cairo's own UI icons. A pack can override any of these via "icons" or icons/&lt;Key&gt;.png.</summary>
        public static readonly string[] IconKeys =
        {
            "MenuIcon", "SearchIcon", "ActionCenterIcon", "VolumeIcon", "VolumeLowIcon", "VolumeOffIcon",
            "VolumeMuteIcon", "DateTimeIcon", "DesktopOverlayIcon", "TaskListMenuIcon", "TaskViewIcon",
            "DesktopToolbarBackIcon", "DesktopToolbarUpIcon", "DesktopToolbarForwardIcon",
            "DesktopToolbarBrowseIcon", "DesktopToolbarHomeIcon"
        };

        private static readonly string[] IconExtensions = { ".png", ".ico", ".jpg", ".bmp" };

        /// <param name="findExisting">Looks up the current (pre-layer) value of a resource key, used to type raw overrides.</param>
        public static ResourceDictionary Build(ThemePack pack, Func<string, object> findExisting, List<string> warnings)
        {
            var layer = new ResourceDictionary();
            var manifest = pack.Manifest;

            if (manifest.Palette != null)
            {
                var expansion = PaletteExpander.Expand(manifest.Palette);
                warnings.AddRange(expansion.Warnings);
                foreach (var entry in expansion.Brushes)
                {
                    layer[entry.Key] = Freeze(new SolidColorBrush(entry.Value));
                }
            }

            if (manifest.DarkMode.HasValue)
            {
                layer["EnableDarkMode"] = manifest.DarkMode.Value;
            }

            if (manifest.Resources != null)
            {
                foreach (var entry in manifest.Resources)
                {
                    object value = ConvertRawResource(entry.Key, entry.Value, findExisting(entry.Key), out string error);
                    if (value != null)
                    {
                        layer[entry.Key] = value;
                    }
                    else
                    {
                        warnings.Add($"resources.{entry.Key}: {error}");
                    }
                }
            }

            ApplyFont(pack, layer, warnings);
            ApplyIcons(pack, layer, warnings);

            if (!string.IsNullOrWhiteSpace(manifest.Xaml))
            {
                var extra = LoadXaml(pack, manifest.Xaml, warnings);
                if (extra != null)
                {
                    layer.MergedDictionaries.Add(extra);
                }
            }

            return layer;
        }

        private static void ApplyFont(ThemePack pack, ResourceDictionary layer, List<string> warnings)
        {
            var font = pack.Manifest.Font;
            if (font == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(font.Family))
            {
                FontFamily family;
                if (!string.IsNullOrWhiteSpace(font.File))
                {
                    string path = pack.ResolvePath(font.File);
                    if (path == null || !File.Exists(path))
                    {
                        warnings.Add($"font.file '{font.File}' was not found in the pack.");
                        family = new FontFamily(font.Family);
                    }
                    else
                    {
                        // Private font: WPF loads it straight from the folder; nothing is installed.
                        string dir = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;
                        family = new FontFamily(new Uri(dir), "./#" + font.Family);
                    }
                }
                else
                {
                    family = new FontFamily(font.Family);
                }

                layer["GlobalFontFamily"] = family;
                layer["DialogFontFamily"] = family;
            }

            if (font.Size.HasValue && font.Size.Value > 0)
            {
                double size = font.Size.Value;
                layer["SmallFontSize"] = Math.Round(size * 10 / 12, 1);
                layer["MediumFontSize"] = size;
                layer["LargeFontSize"] = Math.Round(size * 14 / 12, 1);
                layer["MediumTitleFontSize"] = Math.Round(size * 20 / 12, 1);
                layer["LargeTitleFontSize"] = Math.Round(size * 33 / 12, 1);
            }
        }

        private static void ApplyIcons(ThemePack pack, ResourceDictionary layer, List<string> warnings)
        {
            var explicitIcons = pack.Manifest.Icons ?? new Dictionary<string, string>();

            foreach (string key in IconKeys)
            {
                string path = null;
                if (explicitIcons.TryGetValue(key, out string rel))
                {
                    path = pack.ResolvePath(rel);
                    if (path == null || !File.Exists(path))
                    {
                        warnings.Add($"icons.{key}: '{rel}' was not found in the pack.");
                        continue;
                    }
                }
                else
                {
                    // Convention: icons/<Key>.png
                    foreach (string ext in IconExtensions)
                    {
                        string candidate = Path.Combine(pack.Folder, "icons", key + ext);
                        if (File.Exists(candidate))
                        {
                            path = candidate;
                            break;
                        }
                    }
                }

                if (path != null)
                {
                    var image = LoadImage(path, out string error);
                    if (image != null)
                    {
                        layer[key] = image;
                    }
                    else
                    {
                        warnings.Add($"icons.{key}: {error}");
                    }
                }
            }

            foreach (string key in explicitIcons.Keys)
            {
                if (Array.IndexOf(IconKeys, key) < 0)
                {
                    warnings.Add($"icons.{key}: not a Cairo icon key (known: {string.Join(", ", IconKeys)}).");
                }
            }
        }

        /// <summary>Loads an image fully into memory so the file isn't locked (theme folders are edited live).</summary>
        public static BitmapImage LoadImage(string path, out string error)
        {
            error = null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static ResourceDictionary LoadXaml(ThemePack pack, string relative, List<string> warnings)
        {
            string path = pack.ResolvePath(relative);
            if (path == null || !File.Exists(path))
            {
                warnings.Add($"xaml '{relative}' was not found in the pack.");
                return null;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var context = new ParserContext { BaseUri = new Uri(path, UriKind.Absolute) };
                    if (XamlReader.Load(stream, context) is ResourceDictionary dictionary)
                    {
                        return dictionary;
                    }

                    warnings.Add($"xaml '{relative}' is not a ResourceDictionary.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"xaml '{relative}': {ex.Message}");
            }

            return null;
        }

        /// <summary>Converts a raw "resources" value to the type Cairo uses for that key.</summary>
        internal static object ConvertRawResource(string key, string raw, object existing, out string error)
        {
            error = null;
            try
            {
                switch (existing)
                {
                    case Color _:
                        if (ColorParser.TryParse(raw, out Color c)) return c;
                        error = $"'{raw}' is not a colour.";
                        return null;
                    case double _:
                        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                        error = $"'{raw}' is not a number.";
                        return null;
                    case bool _:
                        if (bool.TryParse(raw, out bool flag)) return flag;
                        error = $"'{raw}' is not true/false.";
                        return null;
                    case Thickness _:
                        return (Thickness)new ThicknessConverter().ConvertFromInvariantString(raw);
                    case CornerRadius _:
                        return (CornerRadius)new CornerRadiusConverter().ConvertFromInvariantString(raw);
                    case FontFamily _:
                        return new FontFamily(raw);
                }

                if (ColorParser.TryParse(raw, out Color brushColor))
                {
                    return Freeze(new SolidColorBrush(brushColor));
                }

                error = existing == null
                    ? $"unknown key and '{raw}' is not a colour."
                    : $"keys of type {existing.GetType().Name} can't be set from a string; use the pack's xaml file.";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
