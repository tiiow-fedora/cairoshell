using System.Collections.Generic;
using System.Text.Json;

namespace CairoDesktop.Customization.Config
{
    /// <summary>
    /// Root of cairo-plus.json. Every property is optional; anything left out keeps Cairo's
    /// stock behaviour (or the active theme pack's value, where the pack sets one).
    /// </summary>
    public sealed class CairoPlusConfig
    {
        /// <summary>Master switch. When false, Cairo Plus does nothing at all.</summary>
        public bool Enabled { get; set; }

        /// <summary>Folder name of the active theme pack, or null for none.</summary>
        public string Theme { get; set; }

        public BarConfig MenuBar { get; set; }

        public BarConfig Taskbar { get; set; }

        public AnimationConfig Animations { get; set; }

        /// <summary>Widget instances by id. Each value is the widget's option object (must contain "type" unless the id is a widget type).</summary>
        public Dictionary<string, JsonElement> Widgets { get; set; }

        public List<HotkeyConfig> Hotkeys { get; set; }

        /// <summary>Per-app icon overrides: exe file name (e.g. "notepad.exe") to an image path.</summary>
        public Dictionary<string, string> AppIcons { get; set; }
    }

    /// <summary>Layout and shape settings for one bar. Used by both the config file and theme manifests.</summary>
    public sealed class BarConfig
    {
        public ZoneLayout Layout { get; set; }

        /// <summary>Float the bar away from the screen edge (uses <see cref="Margin"/> as the gap).</summary>
        public bool? Floating { get; set; }

        /// <summary>Gap in pixels between a floating bar and the screen edges.</summary>
        public double? Margin { get; set; }

        public double? CornerRadius { get; set; }

        /// <summary>Gap in pixels between items in a layout zone.</summary>
        public double? Spacing { get; set; }

        /// <summary>Background opacity multiplier, 0.0 to 1.0.</summary>
        public double? Opacity { get; set; }

        /// <summary>Blur the desktop behind the bar. Only honoured on docked, square bars (see README).</summary>
        public bool? Blur { get; set; }

        internal static BarConfig Merge(BarConfig under, BarConfig over)
        {
            if (under == null) return over;
            if (over == null) return under;

            return new BarConfig
            {
                Layout = over.Layout ?? under.Layout,
                Floating = over.Floating ?? under.Floating,
                Margin = over.Margin ?? under.Margin,
                CornerRadius = over.CornerRadius ?? under.CornerRadius,
                Spacing = over.Spacing ?? under.Spacing,
                Opacity = over.Opacity ?? under.Opacity,
                Blur = over.Blur ?? under.Blur
            };
        }
    }

    /// <summary>Ordered item ids for the three zones of a bar. A null zone list means "not customized".</summary>
    public sealed class ZoneLayout
    {
        public List<string> Left { get; set; }

        public List<string> Center { get; set; }

        public List<string> Right { get; set; }
    }

    public sealed class AnimationConfig
    {
        public bool? Enabled { get; set; }

        public int? DurationMs { get; set; }

        internal static AnimationConfig Merge(AnimationConfig under, AnimationConfig over)
        {
            if (under == null) return over;
            if (over == null) return under;

            return new AnimationConfig
            {
                Enabled = over.Enabled ?? under.Enabled,
                DurationMs = over.DurationMs ?? under.DurationMs
            };
        }
    }

    public sealed class HotkeyConfig
    {
        /// <summary>Key combination, e.g. "Win+Alt+T" or "Ctrl+Shift+F12".</summary>
        public string Keys { get; set; }

        /// <summary>launch, run, theme, command or reload.</summary>
        public string Action { get; set; }

        /// <summary>Action argument: a path, a script command line, a theme name / "next", or a Cairo command name.</summary>
        public string Arg { get; set; }

        /// <summary>Optional arguments for "launch".</summary>
        public string Args { get; set; }
    }
}
