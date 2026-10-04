using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media;
using CairoDesktop.AppGrabber;
using ManagedShell.WindowsTasks;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>
    /// Per-app icon replacements for taskbar and quick launch buttons, keyed by exe file name.
    /// Empty by default, in which case the converter passes Cairo's own icon straight through.
    /// </summary>
    public sealed class AppIconOverrides : INotifyPropertyChanged
    {
        public static AppIconOverrides Instance { get; } = new AppIconOverrides();

        private Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ImageSource> _cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private int _version;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Bumped whenever the map changes; bound by the icon MultiBinding so buttons refresh live.</summary>
        public int Version => _version;

        public bool IsEmpty => _paths.Count == 0;

        /// <summary>Replaces the override map (exe name or full exe path to image path).</summary>
        public void SetOverrides(IDictionary<string, string> map)
        {
            var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (map != null)
            {
                foreach (var entry in map)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        next[entry.Key.Trim()] = entry.Value;
                    }
                }
            }

            _paths = next;
            _cache.Clear();
            _version++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        }

        public ImageSource Lookup(string exePath)
        {
            if (_paths.Count == 0 || string.IsNullOrEmpty(exePath))
            {
                return null;
            }

            string key = null;
            if (_paths.ContainsKey(exePath))
            {
                key = exePath;
            }
            else
            {
                string name = Path.GetFileName(exePath);
                if (!string.IsNullOrEmpty(name) && _paths.ContainsKey(name))
                {
                    key = name;
                }
            }

            if (key == null)
            {
                return null;
            }

            if (!_cache.TryGetValue(key, out ImageSource image))
            {
                image = File.Exists(_paths[key]) ? ThemeLayerBuilder.LoadImage(_paths[key], out _) : null;
                _cache[key] = image;
            }

            return image;
        }

        /// <summary>Finds the executable behind a taskbar or quick launch item.</summary>
        internal static string GetExePath(object item)
        {
            switch (item)
            {
                case ApplicationWindow window:
                    return window.WinFileName;
                case ApplicationInfo app:
                    return !string.IsNullOrEmpty(app.Target) ? app.Target : app.Path;
                case null:
                    return null;
            }

            // Grouped taskbar buttons bind to Cairo's TaskGroup, which exposes its windows as a public field.
            FieldInfo windowsField = item.GetType().GetField("Windows", BindingFlags.Public | BindingFlags.Instance);
            if (windowsField?.GetValue(item) is IEnumerable windows)
            {
                foreach (object w in windows)
                {
                    if (w is ApplicationWindow first)
                    {
                        return first.WinFileName;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// MultiBinding converter: values[0] = Cairo's icon, values[1] = the item, values[2] = override version.
    /// Returns the override image if one matches, otherwise Cairo's icon unchanged.
    /// </summary>
    public sealed class AppIconOverrideConverter : IMultiValueConverter
    {
        public static AppIconOverrideConverter Instance { get; } = new AppIconOverrideConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            object original = values.Length > 0 ? values[0] : null;
            if (original == System.Windows.DependencyProperty.UnsetValue)
            {
                original = null;
            }

            if (values.Length > 1 && !AppIconOverrides.Instance.IsEmpty)
            {
                var replacement = AppIconOverrides.Instance.Lookup(AppIconOverrides.GetExePath(values[1]));
                if (replacement != null)
                {
                    return replacement;
                }
            }

            return original;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
