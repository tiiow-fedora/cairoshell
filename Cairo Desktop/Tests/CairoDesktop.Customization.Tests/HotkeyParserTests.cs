using System.Windows.Input;
using CairoDesktop.Common;
using CairoDesktop.Customization.Hotkeys;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class HotkeyParserTests
    {
        [Theory]
        [InlineData("Win+Alt+T", HotKeyModifier.Win | HotKeyModifier.Alt, Key.T)]
        [InlineData("ctrl + shift + f12", HotKeyModifier.Ctrl | HotKeyModifier.Shift, Key.F12)]
        [InlineData("Control+Alt+Delete", HotKeyModifier.Ctrl | HotKeyModifier.Alt, Key.Delete)]
        [InlineData("Super+Return", HotKeyModifier.Win, Key.Return)]
        [InlineData("Win+Enter", HotKeyModifier.Win, Key.Return)]
        [InlineData("Meta+Space", HotKeyModifier.Win, Key.Space)]
        [InlineData("Ctrl+Alt+1", HotKeyModifier.Ctrl | HotKeyModifier.Alt, Key.D1)]
        [InlineData("Alt+Shift+Left", HotKeyModifier.Alt | HotKeyModifier.Shift, Key.Left)]
        [InlineData("Ctrl+Alt+Plus", HotKeyModifier.Ctrl | HotKeyModifier.Alt, Key.OemPlus)]
        [InlineData("Ctrl++", HotKeyModifier.Ctrl, Key.OemPlus)]
        [InlineData("Win+,", HotKeyModifier.Win, Key.OemComma)]
        [InlineData("T+Win", HotKeyModifier.Win, Key.T)]
        [InlineData("Win+Win+T", HotKeyModifier.Win, Key.T)]
        [InlineData("F13", HotKeyModifier.None, Key.F13)]
        [InlineData("MediaPlayPause", HotKeyModifier.None, Key.MediaPlayPause)]
        public void Parses_valid_hotkeys(string text, HotKeyModifier modifiers, Key key)
        {
            Assert.True(HotkeyParser.TryParse(text, out HotKeyModifier parsedModifiers, out Key parsedKey, out string error), error);
            Assert.Equal(modifiers, parsedModifiers);
            Assert.Equal(key, parsedKey);
        }

        [Theory]
        [InlineData("", "no keys")]
        [InlineData("   ", "no keys")]
        [InlineData("Win+Alt", "no key")]
        [InlineData("Win+T+Y", "more than one")]
        [InlineData("Win+Banana", "not a key name")]
        [InlineData("Win+65", "not a key name")]
        [InlineData("Win++T", "empty key")]
        [InlineData("T", "needs a modifier")]
        [InlineData("5", "needs a modifier")]
        public void Rejects_invalid_hotkeys_with_a_reason(string text, string expectedReason)
        {
            Assert.False(HotkeyParser.TryParse(text, out _, out _, out string error));
            Assert.Contains(expectedReason, error);
        }

        [Theory]
        [InlineData("alt+win+t", "Win+Alt+T")]
        [InlineData("shift+ctrl+1", "Ctrl+Shift+1")]
        [InlineData("super+ctrl+alt+shift+f5", "Win+Ctrl+Alt+Shift+F5")]
        public void Formats_in_canonical_order(string text, string expected)
        {
            Assert.True(HotkeyParser.TryParse(text, out HotKeyModifier modifiers, out Key key, out _));
            Assert.Equal(expected, HotkeyParser.Format(modifiers, key));
        }
    }
}
