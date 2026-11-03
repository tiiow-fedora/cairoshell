using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.MonitorProfiles
{
    public enum TaskbarEdge { Top, Bottom, Left, Right }

    public enum ThemeVariant { Auto, Light, Dark }

    public enum MonitorOrientation { Landscape, Portrait }

    /// <summary>
    /// Taskbar settings one layer (built-in, default, or a rule) may set. Every property is nullable:
    /// null means "not set here, inherit from the layer below".
    /// </summary>
    public sealed class ProfileSettings
    {
        /// <summary>JSON / explanation names of every property, in display order.</summary>
        public static readonly IReadOnlyList<string> PropertyNames = new[]
        {
            "position", "height", "autoHide", "floating", "margin", "layoutName",
            "themeVariant", "showClock", "showTaskList", "iconSize", "hideOnFullscreen",
        };

        public TaskbarEdge? Position { get; set; }
        public int? Height { get; set; }
        public bool? AutoHide { get; set; }
        public bool? Floating { get; set; }
        public int? Margin { get; set; }
        public string LayoutName { get; set; }
        public ThemeVariant? ThemeVariant { get; set; }
        public bool? ShowClock { get; set; }
        public bool? ShowTaskList { get; set; }
        public int? IconSize { get; set; }
        public bool? HideOnFullscreen { get; set; }

        /// <summary>The bottom layer: what every monitor gets when nothing else says otherwise. layoutName stays unset.</summary>
        public static ProfileSettings BuiltInDefaults() => new ProfileSettings
        {
            Position = TaskbarEdge.Bottom,
            Height = 30,
            AutoHide = false,
            Floating = false,
            Margin = 0,
            ThemeVariant = MonitorProfiles.ThemeVariant.Auto,
            ShowClock = true,
            ShowTaskList = true,
            IconSize = 24,
            HideOnFullscreen = false,
        };

        public object Get(string name)
        {
            switch (Canon(name))
            {
                case "position": return Position;
                case "height": return Height;
                case "autoHide": return AutoHide;
                case "floating": return Floating;
                case "margin": return Margin;
                case "layoutName": return LayoutName;
                case "themeVariant": return ThemeVariant;
                case "showClock": return ShowClock;
                case "showTaskList": return ShowTaskList;
                case "iconSize": return IconSize;
                case "hideOnFullscreen": return HideOnFullscreen;
                default: throw new ArgumentException("Unknown profile property '" + name + "'.", nameof(name));
            }
        }

        /// <summary>Sets a property from a boxed value of the right type (or null to unset).</summary>
        public void Set(string name, object value)
        {
            switch (Canon(name))
            {
                case "position": Position = (TaskbarEdge?)value; break;
                case "height": Height = (int?)value; break;
                case "autoHide": AutoHide = (bool?)value; break;
                case "floating": Floating = (bool?)value; break;
                case "margin": Margin = (int?)value; break;
                case "layoutName": LayoutName = (string)value; break;
                case "themeVariant": ThemeVariant = (ThemeVariant?)value; break;
                case "showClock": ShowClock = (bool?)value; break;
                case "showTaskList": ShowTaskList = (bool?)value; break;
                case "iconSize": IconSize = (int?)value; break;
                case "hideOnFullscreen": HideOnFullscreen = (bool?)value; break;
                default: throw new ArgumentException("Unknown profile property '" + name + "'.", nameof(name));
            }
        }

        public bool IsSet(string name) => Get(name) != null;

        public IEnumerable<string> SetPropertyNames()
        {
            foreach (string n in PropertyNames) if (IsSet(n)) yield return n;
        }

        /// <summary>Human readable value of a property ("(unset)" when null).</summary>
        public string Format(string name)
        {
            object v = Get(name);
            if (v == null) return "(unset)";
            if (v is bool b) return b ? "true" : "false";
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public ProfileSettings Clone() => (ProfileSettings)MemberwiseClone();

        public bool ValueEquals(ProfileSettings other)
        {
            if (other == null) return false;
            foreach (string n in PropertyNames) if (!Equals(Get(n), other.Get(n))) return false;
            return true;
        }

        internal static string Canon(string name)
        {
            foreach (string n in PropertyNames) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return n;
            return name;
        }
    }
}
