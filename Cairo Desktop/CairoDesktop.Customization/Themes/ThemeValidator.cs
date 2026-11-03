using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    public enum IssueSeverity
    {
        /// <summary>Something that will not work (missing file, invalid value).</summary>
        Error,

        /// <summary>Probably a mistake, or hard to read (typo'd key, low contrast).</summary>
        Warning,

        /// <summary>Worth knowing, no action needed.</summary>
        Info
    }

    public sealed class ThemeIssue
    {
        public IssueSeverity Severity { get; }

        /// <summary>Where in theme.json, e.g. "palette.text" or "menuBar.layout.left".</summary>
        public string Location { get; }

        public string Message { get; }

        public ThemeIssue(IssueSeverity severity, string location, string message)
        {
            Severity = severity;
            Location = location;
            Message = message;
        }

        public override string ToString() => $"{Severity.ToString().ToLowerInvariant()}: {(string.IsNullOrEmpty(Location) ? "" : Location + ": ")}{Message}";
    }

    public sealed class ThemeValidationOptions
    {
        /// <summary>Widget types beyond the built-in ones (for example from installed widget DLLs).</summary>
        public IEnumerable<string> ExtraWidgetTypes { get; set; }

        /// <summary>Resource keys that "resources" may use. Defaults to <see cref="ThemeSchema.ResourceKeys"/>.</summary>
        public ISet<string> ResourceKeys { get; set; }
    }

    /// <summary>
    /// Lints a theme pack: finds broken file references, bad values, unreadable colour combinations and typos,
    /// without needing any UI. Used by <c>--validate-theme</c> and by the tests that guard the bundled packs.
    /// </summary>
    public static class ThemeValidator
    {
        private const double MinTextContrast = 4.5;
        private const double MinUiContrast = 3.0;

        private static readonly string[] FontExtensions = { ".ttf", ".otf", ".ttc" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
        private static readonly string[] IconExtensions = { ".png", ".ico", ".jpg", ".jpeg", ".bmp" };

        public static List<ThemeIssue> Validate(ThemePack pack, ThemeValidationOptions options = null)
        {
            var issues = new List<ThemeIssue>();
            options = options ?? new ThemeValidationOptions();

            if (pack.Error != null || pack.Manifest == null)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, "", pack.Error ?? "theme.json could not be read."));
                return issues;
            }

            var manifest = pack.Manifest;

            CheckMetadata(pack, manifest, issues);
            CheckPalette(manifest, issues);
            CheckResources(manifest, options, issues);
            CheckFont(pack, manifest, issues);
            CheckWallpaper(pack, manifest, issues);
            CheckIcons(pack, manifest, issues);

            if (!string.IsNullOrWhiteSpace(manifest.Xaml))
            {
                CheckFile(pack, manifest.Xaml, "xaml", null, issues);
            }

            var widgetIds = new HashSet<string>(manifest.Widgets?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var widgetTypes = new HashSet<string>(ThemeSchema.BuiltInWidgetTypes.Concat(options.ExtraWidgetTypes ?? Enumerable.Empty<string>()), StringComparer.OrdinalIgnoreCase);

            CheckBar("menuBar", manifest.MenuBar, ThemeSchema.MenuBarItems, strictIds: false, widgetIds, widgetTypes, issues);
            CheckBar("taskbar", manifest.Taskbar, ThemeSchema.TaskbarItems, strictIds: true, widgetIds, widgetTypes, issues);
            CheckAnimations(manifest.Animations, issues);
            CheckWidgets(manifest, widgetTypes, issues);
            CheckHooks(manifest.Hooks, issues);
            CheckVariant("light", pack, manifest, true, options, issues);
            CheckVariant("dark", pack, manifest, false, options, issues);

            return issues;
        }

        /// <summary>
        /// Checks a light or dark variant as it will look when applied (pack plus variant), reporting only the
        /// findings the plain pack doesn't already have, under "light." or "dark.".
        /// </summary>
        private static void CheckVariant(string name, ThemePack pack, ThemePackManifest manifest, bool light, ThemeValidationOptions options, List<ThemeIssue> issues)
        {
            ThemeVariant variant = light ? manifest.Light : manifest.Dark;
            if (variant == null) return;

            if (variant.IsEmpty)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, name, "sets nothing, so the pack is used unchanged."));
                return;
            }

            var effective = ThemeAppearance.Apply(manifest, light);
            var found = new List<ThemeIssue>();
            CheckPalette(effective, found);
            CheckResources(effective, options, found);
            CheckWallpaper(pack, effective, found);

            var seen = new HashSet<string>(issues.Select(i => i.ToString()), StringComparer.Ordinal);
            foreach (var issue in found)
            {
                if (seen.Contains(issue.ToString())) continue;
                string location = string.IsNullOrEmpty(issue.Location) ? name : name + "." + issue.Location;
                issues.Add(new ThemeIssue(issue.Severity, location, issue.Message));
            }
        }

        private static void CheckMetadata(ThemePack pack, ThemePackManifest manifest, List<ThemeIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, "name", $"not set; the folder name '{pack.Id}' is shown instead."));
            }

            if (string.IsNullOrWhiteSpace(manifest.Description))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, "description", "not set."));
            }

            if (!string.IsNullOrWhiteSpace(manifest.BaseTheme) && !ThemeSchema.BaseThemes.Contains(manifest.BaseTheme.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, "baseTheme",
                    $"'{manifest.BaseTheme}' is not one of Cairo's bundled XAML themes ({string.Join(", ", ThemeSchema.BaseThemes)}). Fine if it is a custom theme in %LOCALAPPDATA%\\Cairo Desktop\\Themes."));
            }
        }

        private static void CheckPalette(ThemePackManifest manifest, List<ThemeIssue> issues)
        {
            if (manifest.Palette == null || manifest.Palette.Count == 0)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, "palette", "not set; Cairo's own colours stay in use."));
                return;
            }

            var expansion = PaletteExpander.Expand(manifest.Palette);
            foreach (string warning in expansion.Warnings)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "palette", warning));
            }

            if (!manifest.Palette.Keys.Any(k => string.Equals(k, "background", StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "palette.background", "not set; everything is derived from a default dark grey."));
            }

            var roles = expansion.Roles;
            AddContrast(issues, "palette.text", "text", roles["text"], "background", roles["background"], MinTextContrast);
            AddContrast(issues, "palette.text", "text", roles["text"], "surface", roles["surface"], MinTextContrast);
            AddContrast(issues, "palette.subtext", "subtext", roles["subtext"], "background", roles["background"], MinUiContrast);
            AddContrast(issues, "palette.accentText", "accentText", roles["accentText"], "accent", roles["accent"], MinTextContrast);

            if (manifest.DarkMode.HasValue)
            {
                bool backgroundIsDark = ColorParser.IsDark(roles["background"]);
                if (manifest.DarkMode.Value != backgroundIsDark)
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Warning, "darkMode",
                        $"is {(manifest.DarkMode.Value ? "true" : "false")} but the background colour is {(backgroundIsDark ? "dark" : "light")}."));
                }
            }
        }

        private static void AddContrast(List<ThemeIssue> issues, string location, string foregroundName,
            System.Windows.Media.Color foreground, string backgroundName, System.Windows.Media.Color background, double minimum)
        {
            double ratio = ColorParser.ContrastRatio(foreground, background);
            if (ratio < minimum)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, location,
                    $"{foregroundName} on {backgroundName} has a contrast ratio of {ratio:0.0}:1 (aim for at least {minimum:0.#}:1)."));
            }
        }

        private static void CheckResources(ThemePackManifest manifest, ThemeValidationOptions options, List<ThemeIssue> issues)
        {
            if (manifest.Resources == null) return;

            var known = options.ResourceKeys ?? ThemeSchema.ResourceKeys;
            foreach (var entry in manifest.Resources)
            {
                if (!known.Contains(entry.Key))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Warning, "resources." + entry.Key, "is not a resource key in Cairo.xaml (typo?)."));
                }

                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Error, "resources." + entry.Key, "has an empty value."));
                }
            }
        }

        private static void CheckFont(ThemePack pack, ThemePackManifest manifest, List<ThemeIssue> issues)
        {
            var font = manifest.Font;
            if (font == null) return;

            if (font.Size.HasValue && (font.Size.Value < 6 || font.Size.Value > 48))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "font.size", $"{font.Size.Value} is outside the sensible range of 6 to 48."));
            }

            if (!string.IsNullOrWhiteSpace(font.File))
            {
                CheckFile(pack, font.File, "font.file", FontExtensions, issues);
                if (string.IsNullOrWhiteSpace(font.Family))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Warning, "font.family", "is needed with font.file; it must match the font's internal family name."));
                }
            }
            else if (string.IsNullOrWhiteSpace(font.Family) && !font.Size.HasValue)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, "font", "sets nothing."));
            }
        }

        private static void CheckWallpaper(ThemePack pack, ThemePackManifest manifest, List<ThemeIssue> issues)
        {
            var wallpaper = manifest.Wallpaper;
            if (wallpaper == null) return;

            if (string.IsNullOrWhiteSpace(wallpaper.File))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "wallpaper.file", "is not set."));
            }
            else
            {
                CheckFile(pack, wallpaper.File, "wallpaper.file", ImageExtensions, issues);
            }

            if (!string.IsNullOrWhiteSpace(wallpaper.Style) && !ThemeSchema.IsKnownWallpaperStyle(wallpaper.Style))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "wallpaper.style",
                    $"'{wallpaper.Style}' is not a wallpaper style (use {string.Join(", ", ThemeSchema.WallpaperStyles)}); fill is used."));
            }
        }

        private static void CheckIcons(ThemePack pack, ThemePackManifest manifest, List<ThemeIssue> issues)
        {
            if (manifest.Icons != null)
            {
                foreach (var entry in manifest.Icons)
                {
                    if (!ThemeSchema.IsKnownIcon(entry.Key))
                    {
                        issues.Add(new ThemeIssue(IssueSeverity.Warning, "icons." + entry.Key, $"is not a Cairo icon key (known: {string.Join(", ", ThemeSchema.IconKeys)})."));
                    }

                    CheckFile(pack, entry.Value, "icons." + entry.Key, IconExtensions, issues);
                }
            }

            if (manifest.AppIcons != null)
            {
                foreach (var entry in manifest.AppIcons)
                {
                    CheckFile(pack, entry.Value, "appIcons." + entry.Key, IconExtensions, issues);
                }
            }
        }

        private static void CheckFile(ThemePack pack, string relative, string location, string[] extensions, List<ThemeIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, location, "has an empty path."));
                return;
            }

            string full = pack.ResolvePath(relative);
            if (full == null)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, location, $"'{relative}' points outside the theme pack folder, which is not allowed."));
                return;
            }

            if (!File.Exists(full))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, location, $"file '{relative}' was not found in the pack."));
                return;
            }

            if (extensions != null && !extensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, location, $"'{relative}' is not a {string.Join("/", extensions)} file."));
            }
        }

        private static void CheckBar(string name, BarConfig bar, string[] builtInItems, bool strictIds,
            HashSet<string> widgetIds, HashSet<string> widgetTypes, List<ThemeIssue> issues)
        {
            if (bar == null) return;

            CheckNumber(issues, name + ".opacity", bar.Opacity, 0, 1, IssueSeverity.Error);
            CheckNumber(issues, name + ".cornerRadius", bar.CornerRadius, 0, 100, IssueSeverity.Warning);
            CheckNumber(issues, name + ".margin", bar.Margin, 0, 200, IssueSeverity.Warning);
            CheckNumber(issues, name + ".spacing", bar.Spacing, 0, 100, IssueSeverity.Warning);

            if (bar.Margin.HasValue && bar.Margin.Value > 0 && bar.Floating != true)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, name + ".margin", "has no effect unless \"floating\" is true."));
            }

            if (bar.Blur == true && (bar.Floating == true || bar.CornerRadius > 0))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, name + ".blur", "is turned off at runtime on floating or rounded bars (Windows limitation)."));
            }

            var layout = bar.Layout;
            if (layout == null) return;

            var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zone in new[] { ("left", layout.Left), ("center", layout.Center), ("right", layout.Right) })
            {
                if (zone.Item2 == null) continue;
                string location = $"{name}.layout.{zone.Item1}";

                foreach (string id in zone.Item2)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        issues.Add(new ThemeIssue(IssueSeverity.Error, location, "contains an empty item id."));
                        continue;
                    }

                    if (placed.TryGetValue(id, out string firstZone))
                    {
                        issues.Add(new ThemeIssue(IssueSeverity.Warning, location,
                            firstZone == zone.Item1 ? $"'{id}' is listed twice." : $"'{id}' is also placed in {firstZone}; only one place is used."));
                        continue;
                    }

                    placed[id] = zone.Item1;
                    CheckLayoutItem(location, id, builtInItems, strictIds, widgetIds, widgetTypes, issues);
                }
            }
        }

        private static void CheckLayoutItem(string location, string id, string[] builtInItems, bool strictIds,
            HashSet<string> widgetIds, HashSet<string> widgetTypes, List<ThemeIssue> issues)
        {
            const string widgetPrefix = "widget:";
            if (id.StartsWith(widgetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string widgetId = id.Substring(widgetPrefix.Length);
                if (widgetId.Length == 0)
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Error, location, "'widget:' needs a widget id."));
                }
                else if (!widgetIds.Contains(widgetId) && !widgetTypes.Contains(widgetId))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Warning, location,
                        $"'{id}' is not defined under \"widgets\" and is not a widget type (built-in: {string.Join(", ", ThemeSchema.BuiltInWidgetTypes)}); your cairo-plus.json may define it."));
                }

                return;
            }

            if (builtInItems.Contains(id, StringComparer.OrdinalIgnoreCase)) return;

            if (strictIds)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, location, $"'{id}' is not a taskbar item (use {string.Join(", ", builtInItems)} or widget:<id>)."));
            }
            else
            {
                issues.Add(new ThemeIssue(IssueSeverity.Info, location,
                    $"'{id}' is not a built-in menu bar item; it only works for a third-party menu extra named that way."));
            }
        }

        private static void CheckNumber(List<ThemeIssue> issues, string location, double? value, double min, double max, IssueSeverity severity)
        {
            if (value.HasValue && (value.Value < min || value.Value > max || double.IsNaN(value.Value)))
            {
                issues.Add(new ThemeIssue(severity, location, $"{value.Value} is outside {min:0.##} to {max:0.##}."));
            }
        }

        private static void CheckAnimations(AnimationConfig animations, List<ThemeIssue> issues)
        {
            if (animations?.DurationMs != null && (animations.DurationMs < 0 || animations.DurationMs > 5000))
            {
                issues.Add(new ThemeIssue(IssueSeverity.Warning, "animations.durationMs", $"{animations.DurationMs} ms is outside 0 to 5000."));
            }
        }

        private static void CheckWidgets(ThemePackManifest manifest, HashSet<string> widgetTypes, List<ThemeIssue> issues)
        {
            if (manifest.Widgets == null) return;

            foreach (var entry in manifest.Widgets)
            {
                string location = "widgets." + entry.Key;
                var element = entry.Value;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Error, location, "must be an object such as { \"type\": \"clock\" }."));
                    continue;
                }

                string type = entry.Key;
                if (element.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    type = typeElement.GetString();
                }

                if (!widgetTypes.Contains(type))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Warning, location,
                        $"type '{type}' is not built in ({string.Join(", ", ThemeSchema.BuiltInWidgetTypes)}); it needs a widget DLL that provides it."));
                }

                if (string.Equals(type, "command", StringComparison.OrdinalIgnoreCase) &&
                    !(element.TryGetProperty("command", out JsonElement command) && command.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(command.GetString())))
                {
                    issues.Add(new ThemeIssue(IssueSeverity.Error, location, "a command widget needs a \"command\"."));
                }

                if (element.TryGetProperty("onClick", out JsonElement onClick))
                {
                    CheckOnClick(location + ".onClick", onClick, issues);
                }
            }
        }

        private static void CheckOnClick(string location, JsonElement onClick, List<ThemeIssue> issues)
        {
            if (onClick.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, location, "must be an object such as { \"action\": \"launch\", \"arg\": \"taskmgr.exe\" }."));
                return;
            }

            string action = onClick.TryGetProperty("action", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            string arg = onClick.TryGetProperty("arg", out JsonElement g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
            string error = ValidateAction(action, arg);
            if (error != null)
            {
                issues.Add(new ThemeIssue(IssueSeverity.Error, location, error));
            }
        }

        /// <summary>Same rules as the runtime action runner (hotkeys and widget clicks).</summary>
        public static string ValidateAction(string action, string arg)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "launch":
                case "run":
                case "command":
                    return string.IsNullOrWhiteSpace(arg) ? $"action '{action}' needs an \"arg\"." : null;
                case "theme":
                case "reload":
                    return null;
                default:
                    return $"unknown action '{action}' (use launch, run, theme, command, reload).";
            }
        }

        private static void CheckHooks(HooksConfig hooks, List<ThemeIssue> issues)
        {
            if (hooks == null || hooks.IsEmpty) return;

            issues.Add(new ThemeIssue(IssueSeverity.Info, "hooks", "run programs on your PC, so they only run when cairo-plus.json has \"allowPackHooks\": true."));

            foreach (string name in ThemeSchema.HookNames)
            {
                var list = hooks.Get(name);
                for (int i = 0; i < list.Count; i++)
                {
                    string error = HookAction.Validate(list[i]);
                    if (error != null)
                    {
                        issues.Add(new ThemeIssue(IssueSeverity.Error, $"hooks.{name}[{i}]", error));
                    }
                }
            }
        }
    }
}
