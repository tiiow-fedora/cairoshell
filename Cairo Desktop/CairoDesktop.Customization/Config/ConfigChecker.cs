using System.Collections.Generic;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Config
{
    /// <summary>Finds mistakes in cairo-plus.json that parse fine but would silently do nothing.</summary>
    public static class ConfigChecker
    {
        public static List<string> Check(CairoPlusConfig config)
        {
            var problems = new List<string>();
            if (config == null) return problems;

            if (!ThemeAppearance.TryParseMode(config.Appearance, out _))
            {
                problems.Add($"\"appearance\": '{config.Appearance}' is not one of {string.Join(", ", ThemeAppearance.ModeNames)}; auto is used.");
            }

            if (config.Monitors != null)
            {
                foreach (var entry in config.Monitors)
                {
                    string error = MonitorOverrides.ValidateKey(entry.Key);
                    if (error != null) problems.Add($"\"monitors\": {error}");
                    else if (entry.Value == null || (entry.Value.MenuBar == null && entry.Value.Taskbar == null))
                    {
                        problems.Add($"\"monitors\": '{entry.Key}' sets neither menuBar nor taskbar.");
                    }
                }
            }

            return problems;
        }
    }
}
