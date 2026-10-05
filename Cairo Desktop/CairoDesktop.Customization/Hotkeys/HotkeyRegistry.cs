using System;
using System.Collections.Generic;
using CairoDesktop.Common;
using CairoDesktop.Customization.Actions;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Hotkeys
{
    /// <summary>
    /// Registers the hotkeys from cairo-plus.json as system-wide hotkeys. Unlike Cairo's built-in
    /// Win+R / Win+D handling, these are registered whether or not Cairo is running as the shell.
    /// </summary>
    internal sealed class HotkeyRegistry
    {
        private readonly List<HotKey> _registered = new List<HotKey>();

        /// <summary>Hotkeys that are currently active, as canonical strings (for the settings tab).</summary>
        public List<string> Active { get; } = new List<string>();

        /// <summary>Replaces all registered hotkeys. Must run on the UI thread (hotkey messages arrive there).</summary>
        public void Apply(IList<HotkeyConfig> hotkeys, ActionRunner actions, Action<string> problem)
        {
            Clear();
            if (hotkeys == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hotkey in hotkeys)
            {
                if (hotkey == null) continue;

                if (!HotkeyParser.TryParse(hotkey.Keys, out HotKeyModifier modifiers, out System.Windows.Input.Key key, out string error))
                {
                    problem($"hotkey \"{hotkey.Keys}\": {error}");
                    continue;
                }

                string name = HotkeyParser.Format(modifiers, key);
                string actionError = ActionRunner.Validate(hotkey.Action, hotkey.Arg);
                if (actionError != null)
                {
                    problem($"hotkey {name}: {actionError}");
                    continue;
                }

                if (!seen.Add(name))
                {
                    problem($"hotkey {name} is defined more than once; only the first is used.");
                    continue;
                }

                var config = hotkey;
                var registered = new HotKey(key, modifiers | HotKeyModifier.NoRepeat, _ =>
                {
                    string runError = actions.Run(config.Action, config.Arg, config.Args);
                    if (runError != null) problem($"hotkey {name}: {runError}");
                }, register: false);

                if (!registered.Register())
                {
                    problem($"hotkey {name} is already in use by Windows or another program.");
                    continue;
                }

                _registered.Add(registered);
                Active.Add($"{name} → {config.Action}{(string.IsNullOrEmpty(config.Arg) ? "" : " " + config.Arg)}");
            }
        }


        public void Clear()
        {
            foreach (var hotKey in _registered)
            {
                hotKey.Unregister();
            }

            _registered.Clear();
            Active.Clear();
        }
    }
}
