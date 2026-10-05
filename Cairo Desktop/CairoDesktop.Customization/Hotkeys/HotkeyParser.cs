using System;
using System.Collections.Generic;
using System.Windows.Input;
using CairoDesktop.Common;

namespace CairoDesktop.Customization.Hotkeys
{
    /// <summary>
    /// Parses hotkey strings such as "Win+Alt+T", "ctrl + shift + F12" or "Ctrl+Alt+Plus".
    /// Modifiers: Win (Windows/Super/Meta), Ctrl (Control), Alt, Shift. Exactly one other key.
    /// </summary>
    public static class HotkeyParser
    {
        private static readonly Dictionary<string, HotKeyModifier> Modifiers = new Dictionary<string, HotKeyModifier>(StringComparer.OrdinalIgnoreCase)
        {
            ["win"] = HotKeyModifier.Win, ["windows"] = HotKeyModifier.Win, ["super"] = HotKeyModifier.Win, ["meta"] = HotKeyModifier.Win,
            ["ctrl"] = HotKeyModifier.Ctrl, ["control"] = HotKeyModifier.Ctrl,
            ["alt"] = HotKeyModifier.Alt,
            ["shift"] = HotKeyModifier.Shift
        };

        private static readonly Dictionary<string, Key> Aliases = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = Key.Return, ["return"] = Key.Return, ["esc"] = Key.Escape, ["space"] = Key.Space,
            ["del"] = Key.Delete, ["ins"] = Key.Insert, ["pgup"] = Key.PageUp, ["pgdn"] = Key.PageDown,
            ["backspace"] = Key.Back, ["plus"] = Key.OemPlus, ["="] = Key.OemPlus, ["minus"] = Key.OemMinus, ["-"] = Key.OemMinus,
            [","] = Key.OemComma, ["comma"] = Key.OemComma, ["."] = Key.OemPeriod, ["period"] = Key.OemPeriod,
            ["/"] = Key.OemQuestion, [";"] = Key.OemSemicolon, ["'"] = Key.OemQuotes, ["`"] = Key.OemTilde,
            ["["] = Key.OemOpenBrackets, ["]"] = Key.OemCloseBrackets, ["\\"] = Key.OemBackslash,
            ["left"] = Key.Left, ["right"] = Key.Right, ["up"] = Key.Up, ["down"] = Key.Down,
            ["printscreen"] = Key.PrintScreen, ["prtsc"] = Key.PrintScreen
        };

        public static bool TryParse(string text, out HotKeyModifier modifiers, out Key key, out string error)
        {
            modifiers = HotKeyModifier.None;
            key = Key.None;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "no keys given.";
                return false;
            }

            // Split on '+', but let a trailing "+" (e.g. "Ctrl++") mean the plus key.
            var parts = new List<string>();
            string remaining = text.Trim();
            if (remaining.EndsWith("++", StringComparison.Ordinal))
            {
                remaining = remaining.Substring(0, remaining.Length - 2);
                parts.Add("plus");
            }

            foreach (string raw in remaining.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0)
                {
                    error = $"'{text}' has an empty key between '+' signs.";
                    return false;
                }

                parts.Add(part);
            }

            foreach (string part in parts)
            {
                if (Modifiers.TryGetValue(part, out HotKeyModifier modifier))
                {
                    modifiers |= modifier;
                    continue;
                }

                if (key != Key.None)
                {
                    error = $"'{text}' has more than one non-modifier key ('{key}' and '{part}').";
                    return false;
                }

                if (!TryParseKey(part, out key))
                {
                    error = $"'{part}' is not a key name (examples: T, F5, Space, Enter, Left, Plus).";
                    return false;
                }
            }

            if (key == Key.None)
            {
                error = $"'{text}' has modifiers but no key.";
                return false;
            }

            if (modifiers == HotKeyModifier.None && !IsSafeWithoutModifier(key))
            {
                error = $"'{text}' needs a modifier (Win, Ctrl, Alt or Shift); a bare '{key}' would block normal typing.";
                return false;
            }

            return true;
        }

        /// <summary>Formats a hotkey back to the canonical "Win+Ctrl+Alt+Shift+Key" form.</summary>
        public static string Format(HotKeyModifier modifiers, Key key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(HotKeyModifier.Win)) parts.Add("Win");
            if (modifiers.HasFlag(HotKeyModifier.Ctrl)) parts.Add("Ctrl");
            if (modifiers.HasFlag(HotKeyModifier.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(HotKeyModifier.Shift)) parts.Add("Shift");
            parts.Add(key >= Key.D0 && key <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
            return string.Join("+", parts);
        }

        private static bool TryParseKey(string part, out Key key)
        {
            key = Key.None;

            if (Aliases.TryGetValue(part, out key)) return true;

            if (part.Length == 1 && char.IsDigit(part[0]))
            {
                key = Key.D0 + (part[0] - '0');
                return true;
            }

            // Enum.TryParse also accepts numbers ("65"), which aren't key names.
            if (part.Length == 0 || char.IsDigit(part[0])) return false;

            return Enum.TryParse(part, true, out key) && Enum.IsDefined(typeof(Key), key) && key != Key.None;
        }

        private static bool IsSafeWithoutModifier(Key key)
        {
            return (key >= Key.F1 && key <= Key.F24) ||
                   key == Key.Pause || key == Key.Scroll ||
                   (key >= Key.BrowserBack && key <= Key.LaunchApplication2);
        }
    }
}
