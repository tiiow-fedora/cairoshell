using System;
using System.Collections.Generic;
using System.Text.Json;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Config
{
    /// <summary>
    /// The effective settings after layering: Cairo defaults &lt; theme pack &lt; cairo-plus.json.
    /// </summary>
    public sealed class ResolvedSettings
    {
        public bool Enabled { get; private set; }

        public ThemePack Pack { get; private set; }

        /// <summary>True when the pack's light variant (if it has one) is in effect.</summary>
        public bool UsesLightAppearance { get; private set; }

        public BarConfig MenuBar { get; private set; }

        public BarConfig Taskbar { get; private set; }

        public AnimationConfig Animations { get; private set; }

        public Dictionary<string, JsonElement> Widgets { get; private set; }

        public List<HotkeyConfig> Hotkeys { get; private set; }

        private Dictionary<string, MonitorConfig> _monitors;

        /// <summary>The bar settings for one monitor: the layered menu bar or taskbar settings plus any matching "monitors" entries.</summary>
        public BarConfig GetBar(bool taskbar, MonitorInfo monitor)
        {
            if (!Enabled) return null;
            return MonitorOverrides.Apply(taskbar ? Taskbar : MenuBar, _monitors, monitor, taskbar);
        }

        /// <summary>Hooks that will run: the pack's (only if allowed) followed by the user's.</summary>
        public HooksConfig Hooks { get; private set; } = new HooksConfig();

        /// <summary>True when the active pack defines hooks that were not run because "allowPackHooks" is off.</summary>
        public bool PackHooksBlocked { get; private set; }

        /// <summary>Whether the active pack may run code: hooks, "command" widgets and its own XAML file all need "allowPackHooks".</summary>
        public bool AllowPackCode { get; private set; }

        /// <summary>True when the active pack has a "xaml" file that is not loaded because "allowPackHooks" is off.</summary>
        public bool PackXamlBlocked { get; private set; }

        /// <summary>Ids of pack-defined "command" widgets that were dropped because "allowPackHooks" is off.</summary>
        public List<string> BlockedPackWidgets { get; private set; } = new List<string>();

        /// <summary>Exe name to absolute image path.</summary>
        public Dictionary<string, string> AppIcons { get; private set; }

        public static readonly ResolvedSettings Disabled = new ResolvedSettings
        {
            Enabled = false,
            Widgets = new Dictionary<string, JsonElement>(),
            Hotkeys = new List<HotkeyConfig>(),
            AppIcons = new Dictionary<string, string>(),
            Hooks = new HooksConfig()
        };

        /// <param name="pack">The pack named by config.Theme, or null. Ignored if it failed to parse.</param>
        /// <param name="windowsUsesLightMode">Whether Windows apps are in light mode; picks the pack's light or dark variant when "appearance" is auto.</param>
        public static ResolvedSettings Resolve(CairoPlusConfig config, ThemePack pack, bool windowsUsesLightMode = false)
        {
            if (config == null || !config.Enabled)
            {
                return Disabled;
            }

            bool light = ThemeAppearance.IsLight(config.Appearance, windowsUsesLightMode);
            pack = ThemeAppearance.Apply(pack, light);
            var manifest = pack?.Manifest;
            var resolved = new ResolvedSettings
            {
                Enabled = true,
                UsesLightAppearance = light,
                _monitors = config.Monitors,
                Pack = manifest != null ? pack : null,
                MenuBar = BarConfig.Merge(manifest?.MenuBar, config.MenuBar),
                Taskbar = BarConfig.Merge(manifest?.Taskbar, config.Taskbar),
                Animations = AnimationConfig.Merge(manifest?.Animations, config.Animations),
                Widgets = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
                Hotkeys = config.Hotkeys ?? new List<HotkeyConfig>(),
                AppIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            bool packHasHooks = manifest?.Hooks != null && !manifest.Hooks.IsEmpty;
            resolved.PackHooksBlocked = packHasHooks && !config.AllowPackHooks;
            resolved.Hooks = HooksConfig.Combine(packHasHooks && config.AllowPackHooks ? manifest.Hooks : null, config.Hooks);

            resolved.AllowPackCode = config.AllowPackHooks;
            resolved.PackXamlBlocked = !config.AllowPackHooks && !string.IsNullOrWhiteSpace(manifest?.Xaml);

            if (manifest?.Widgets != null)
            {
                foreach (var entry in manifest.Widgets)
                {
                    // A "command" widget runs a command line through cmd.exe, so it is as dangerous as a hook.
                    if (!config.AllowPackHooks && IsCommandWidget(entry.Key, entry.Value))
                    {
                        resolved.BlockedPackWidgets.Add(entry.Key);
                        continue;
                    }

                    resolved.Widgets[entry.Key] = entry.Value;
                }
            }

            if (config.Widgets != null)
            {
                foreach (var entry in config.Widgets) resolved.Widgets[entry.Key] = entry.Value;
            }

            if (manifest?.AppIcons != null)
            {
                foreach (var entry in manifest.AppIcons)
                {
                    string path = pack.ResolvePath(entry.Value);
                    if (path != null) resolved.AppIcons[entry.Key] = path;
                }
            }

            if (config.AppIcons != null)
            {
                foreach (var entry in config.AppIcons)
                {
                    resolved.AppIcons[entry.Key] = Environment.ExpandEnvironmentVariables(entry.Value ?? "");
                }
            }

            return resolved;
        }

        private static bool IsCommandWidget(string id, JsonElement definition)
        {
            string type = id;
            if (definition.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in definition.EnumerateObject())
                {
                    if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        type = property.Value.GetString();
                    }
                }
            }

            return string.Equals((type ?? "").Trim(), "command", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The wallpaper path from the active pack, or null.</summary>
        public string GetWallpaperPath()
        {
            string file = Pack?.Manifest.Wallpaper?.File;
            return file == null ? null : Pack.ResolvePath(file);
        }
    }
}
