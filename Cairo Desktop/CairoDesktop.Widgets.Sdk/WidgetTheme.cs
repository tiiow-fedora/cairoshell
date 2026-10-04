using System.Windows;
using System.Windows.Controls;

namespace CairoDesktop.Widgets.Sdk
{
    /// <summary>
    /// Resource keys that follow the active Cairo theme / theme pack. Bind with SetResourceReference so
    /// widgets restyle live when the theme changes.
    /// </summary>
    public static class WidgetTheme
    {
        /// <summary>Theme font family.</summary>
        public const string FontFamilyKey = "GlobalFontFamily";

        /// <summary>Theme base font size.</summary>
        public const string FontSizeKey = "MediumFontSize";

        /// <summary>Accent colour (palette "accent").</summary>
        public const string AccentBrushKey = "MenuItemHoverGradient";

        /// <summary>Subtle track / border colour (palette "border").</summary>
        public const string TrackBrushKey = "MenuBorderBrush";

        /// <summary>Hover background (palette "overlay").</summary>
        public const string HoverBrushKey = "MenuHeaderHoverBackground";

        /// <summary>Foreground text colour for the given bar.</summary>
        public static string ForegroundKey(WidgetBar bar)
        {
            return bar == WidgetBar.Taskbar ? "TaskbarItemForeground" : "MenuHeaderForeground";
        }

        /// <summary>Gives a TextBlock the bar's theme font and colour.</summary>
        public static void ApplyText(TextBlock text, WidgetBar bar)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey(bar));
            text.SetResourceReference(TextBlock.FontFamilyProperty, FontFamilyKey);
            text.SetResourceReference(TextBlock.FontSizeProperty, FontSizeKey);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.None;
        }
    }
}
