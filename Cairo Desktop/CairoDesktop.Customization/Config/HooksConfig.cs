using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Config
{
    /// <summary>One thing to do when a hook fires. Same shape as a hotkey action.</summary>
    public sealed class HookAction
    {
        /// <summary>launch, run or command (theme and reload are not allowed in hooks; they could re-trigger the hook).</summary>
        public string Action { get; set; }

        public string Arg { get; set; }

        /// <summary>Optional arguments for "launch".</summary>
        public string Args { get; set; }

        /// <summary>Returns an error message, or null when the action is usable in a hook.</summary>
        public static string Validate(HookAction hook)
        {
            if (hook == null) return "empty hook entry.";

            string action = (hook.Action ?? "").Trim().ToLowerInvariant();
            if (!ThemeSchema.HookActions.Contains(action))
            {
                return $"unknown hook action '{hook.Action}' (use {string.Join(", ", ThemeSchema.HookActions)}).";
            }

            return string.IsNullOrWhiteSpace(hook.Arg) ? $"hook action '{action}' needs an \"arg\"." : null;
        }
    }

    /// <summary>
    /// Scripts and commands to run when something happens. Lets a theme switch also restyle other programs
    /// (terminal, editor, wallpaper tools): a hook is handed the new theme in CAIRO_* environment variables.
    /// </summary>
    public sealed class HooksConfig
    {
        /// <summary>When Cairo starts, after settings were first applied.</summary>
        public List<HookAction> OnStart { get; set; }

        /// <summary>When the active theme pack changed (a switch, or editing "theme" in cairo-plus.json). Not at startup.</summary>
        public List<HookAction> OnThemeApplied { get; set; }

        /// <summary>Every time settings are re-applied, including theme switches and live-reload edits.</summary>
        public List<HookAction> OnConfigReloaded { get; set; }

        public bool IsEmpty => Count == 0;

        public int Count => (OnStart?.Count ?? 0) + (OnThemeApplied?.Count ?? 0) + (OnConfigReloaded?.Count ?? 0);

        /// <summary>The actions for a hook name from <see cref="ThemeSchema.HookNames"/> (case-insensitive); empty if none.</summary>
        public IReadOnlyList<HookAction> Get(string hookName)
        {
            List<HookAction> list = null;
            if (string.Equals(hookName, "onStart", StringComparison.OrdinalIgnoreCase)) list = OnStart;
            else if (string.Equals(hookName, "onThemeApplied", StringComparison.OrdinalIgnoreCase)) list = OnThemeApplied;
            else if (string.Equals(hookName, "onConfigReloaded", StringComparison.OrdinalIgnoreCase)) list = OnConfigReloaded;
            return list ?? new List<HookAction>();
        }

        /// <summary>Concatenates per hook: everything in <paramref name="first"/> runs before <paramref name="second"/>.</summary>
        public static HooksConfig Combine(HooksConfig first, HooksConfig second)
        {
            if (first == null || first.IsEmpty) return second ?? new HooksConfig();
            if (second == null || second.IsEmpty) return first;

            return new HooksConfig
            {
                OnStart = Concat(first.OnStart, second.OnStart),
                OnThemeApplied = Concat(first.OnThemeApplied, second.OnThemeApplied),
                OnConfigReloaded = Concat(first.OnConfigReloaded, second.OnConfigReloaded)
            };
        }

        private static List<HookAction> Concat(List<HookAction> a, List<HookAction> b)
        {
            return (a ?? new List<HookAction>()).Concat(b ?? new List<HookAction>()).ToList();
        }
    }
}
