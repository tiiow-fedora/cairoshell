using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace CairoDesktop.Customization.Themes
{
    public sealed class PaletteExpansion
    {
        /// <summary>Resolved semantic roles (background, surface, ...), including derived defaults.</summary>
        public Dictionary<string, Color> Roles { get; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cairo resource key to colour. Each becomes a SolidColorBrush in the theme layer.</summary>
        public Dictionary<string, Color> Brushes { get; } = new Dictionary<string, Color>(StringComparer.Ordinal);

        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Expands a small semantic palette into the ~70 brush keys Cairo's XAML themes use.
    /// Roles: background, surface, overlay, border, text, subtext, accent, accentText, urgent.
    /// Only "background" really matters; every other role has a sensible derived default.
    /// </summary>
    public static class PaletteExpander
    {
        public static readonly string[] RoleNames =
        {
            "background", "surface", "overlay", "border", "text", "subtext", "accent", "accentText", "urgent"
        };

        public static PaletteExpansion Expand(IDictionary<string, string> palette)
        {
            var result = new PaletteExpansion();
            var given = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

            if (palette != null)
            {
                foreach (var entry in palette)
                {
                    if (Array.FindIndex(RoleNames, r => string.Equals(r, entry.Key, StringComparison.OrdinalIgnoreCase)) < 0)
                    {
                        result.Warnings.Add($"Unknown palette role '{entry.Key}' (known: {string.Join(", ", RoleNames)}).");
                        continue;
                    }

                    if (ColorParser.TryParse(entry.Value, out Color c))
                    {
                        given[entry.Key] = c;
                    }
                    else
                    {
                        result.Warnings.Add($"Palette role '{entry.Key}': '{entry.Value}' is not a colour.");
                    }
                }
            }

            Color Get(string role, Func<Color> fallback)
            {
                Color c = given.TryGetValue(role, out Color v) ? v : fallback();
                result.Roles[role] = c;
                return c;
            }

            Color background = Get("background", () => Color.FromRgb(0x1E, 0x1E, 0x1E));
            Color text = Get("text", () => ColorParser.IsDark(background) ? Colors.White : Colors.Black);
            Color surface = Get("surface", () => ColorParser.Mix(background, text, 0.06));
            Color overlay = Get("overlay", () => ColorParser.Mix(surface, text, 0.12));
            Color border = Get("border", () => ColorParser.Mix(background, text, 0.2));
            Color subtext = Get("subtext", () => ColorParser.Mix(text, background, 0.35));
            Color accent = Get("accent", () => Color.FromRgb(0x3B, 0x82, 0xF6));
            Color accentText = Get("accentText", () => ColorParser.IsDark(accent) ? Colors.White : Colors.Black);
            Color urgent = Get("urgent", () => Color.FromRgb(0xE5, 0x53, 0x4B));

            Color accentHover = ColorParser.Mix(accent, Colors.White, 0.15);
            Color accentPressed = ColorParser.Mix(accent, Colors.Black, 0.2);

            var b = result.Brushes;

            // Menu bar and menus
            b["MenuBarBackground"] = background;
            b["MenuBarBorder"] = border;
            b["MenuBackgroundGradient"] = surface;
            b["MenuItemHoverGradient"] = accent;
            b["MenuHeaderPressedGradient"] = accent;
            b["MenuHeaderHoverBackground"] = overlay;
            b["ProgramsMenuCategoryHoverBackground"] = overlay;
            b["MenuHeaderForeground"] = text;
            b["MenuItemForeground"] = text;
            b["MenuTopBorderBrush"] = accent;
            b["MenuBorderBrush"] = border;
            b["ProgramsMenuCategoryForeground"] = subtext;
            b["MenuSeparator"] = border;
            b["SysTrayExpanderColor"] = text;
            b["NoItemsForeground"] = subtext;
            b["NotifyBalloonBackground"] = surface;

            // Search
            b["SearchTitleText"] = text;
            b["SearchTitleBackground"] = background;
            b["SearchResultText"] = text;
            b["SearchResultSubtext"] = subtext;
            b["SearchResultBackground0"] = surface;
            b["SearchResultBackground1"] = ColorParser.Mix(surface, text, 0.04);
            b["SearchResultHover"] = ColorParser.WithAlpha(accent, 0x80);
            b["SearchViewAllResultsBackground"] = accent;
            b["SearchViewAllResultsHoverBackground"] = accentHover;
            b["SearchViewAllResultsPressedBackground"] = accentPressed;

            // Desktop
            b["StacksIconText"] = text;
            b["DesktopHoverBackground"] = ColorParser.WithAlpha(accent, 0x40);
            b["DesktopPressedBackground"] = ColorParser.WithAlpha(accent, 0x70);
            b["DesktopNavToolbarBackground"] = background;
            b["DesktopNavToolbarBorder"] = border;

            // Taskbar
            b["TaskbarBottomBackground"] = background;
            b["TaskbarTopBackground"] = background;
            b["TaskbarBottomBorder"] = border;
            b["TaskbarTopBorder"] = border;
            b["TaskGroupBorder"] = border;
            b["TaskbarItemForeground"] = text;
            b["TaskbarItemInactiveBackgroundGradient"] = ColorParser.WithAlpha(text, 0x10);
            b["TaskbarItemInactiveBorderGradient"] = ColorParser.WithAlpha(text, 0x20);
            b["TaskbarItemInactiveHoverBackground"] = overlay;
            b["TaskbarItemInactiveHoverBorder"] = border;
            b["TaskbarItemInactivePressedBackground"] = ColorParser.WithAlpha(accent, 0x60);
            b["TaskbarItemActiveBackground"] = ColorParser.WithAlpha(accent, 0x50);
            b["TaskbarItemActiveBorder"] = accent;
            b["TaskbarItemActiveHoverBackground"] = ColorParser.WithAlpha(accent, 0x70);
            b["TaskbarItemActivePressedBackground"] = ColorParser.WithAlpha(accent, 0x90);
            b["TaskbarItemFlashingBackgroundGradient"] = ColorParser.WithAlpha(urgent, 0x60);
            b["TaskbarItemFlashingBorderGradient"] = urgent;
            b["TaskbarItemFlashingDisabledBackground"] = ColorParser.WithAlpha(urgent, 0xC0);
            b["TaskbarItemFlashingHoverBackground"] = ColorParser.WithAlpha(urgent, 0x80);
            b["TaskbarItemFlashingPressedBackground"] = ColorParser.WithAlpha(urgent, 0xA0);
            b["TaskbarProgressBarFill"] = accent;
            b["TaskThumbInactiveBackground"] = surface;
            b["TaskThumbHoverBackground"] = overlay;
            b["TaskThumbCloseButtonForeground"] = text;
            b["TaskThumbCloseButtonHoverBackground"] = urgent;
            b["TaskThumbCloseButtonPressedBackground"] = ColorParser.Mix(urgent, Colors.Black, 0.2);

            // Dialogs
            b["DialogWindowBackground"] = surface;
            b["DialogWindowForeground"] = text;
            b["DialogMessageForeground"] = text;
            b["DialogButtonNormalBackground"] = overlay;
            b["DialogButtonNormalBorder"] = border;
            b["DialogButtonNormalForeground"] = text;
            b["DialogButtonHoverBackground"] = accent;
            b["DialogButtonHoverBorder"] = accent;
            b["DialogButtonHoverForeground"] = accentText;
            b["DialogButtonPressedBackground"] = accentPressed;
            b["DialogButtonPressedBorder"] = accentPressed;
            b["DialogButtonPressedForeground"] = accentText;
            b["DialogButtonDisabledBackground"] = surface;
            b["DialogButtonDisabledForeground"] = subtext;
            b["DialogButtonDefaultBorder"] = accent;
            b["DisabledColor"] = subtext;

            return result;
        }
    }
}
