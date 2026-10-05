namespace CairoDesktop.Customization.Config
{
    /// <summary>The commented starter file written the first time Cairo Plus is enabled.</summary>
    internal static class ConfigTemplate
    {
        public const string Text = @"// Cairo Plus configuration. Comments and trailing commas are allowed.
// Saved changes are applied live; there is no need to restart Cairo.
// Anything you leave out keeps Cairo's normal behaviour (or the theme pack's value).
{
  // Master switch. false = Cairo Plus does nothing.
  ""enabled"": false,

  // Theme pack folder name (from <Cairo>\ThemePacks or %LOCALAPPDATA%\Cairo Desktop\ThemePacks), or null.
  ""theme"": null,

  // Bar layout. Each zone is an ordered list of item ids; items you don't list are hidden.
  // A zone you leave out keeps its normal contents. ""stacks"" and ""tasks"" stretch when placed in ""center"".
  //   Menu bar items: cairoMenu, programsMenu, placesMenu, stacks, tray, volume, actionCenter, clock, search
  //   Taskbar items:  desktopButton, quickLaunch, tasks, taskList
  //   Widgets:        widget:<id>  (see ""widgets"" below)
  // ""menuBar"": {
  //   ""layout"": {
  //     ""left"":   [""cairoMenu"", ""programsMenu"", ""placesMenu""],
  //     ""center"": [""widget:clock""],
  //     ""right"":  [""widget:cpu"", ""widget:memory"", ""tray"", ""search""],
  //   },
  // },
  // ""taskbar"": { ""layout"": { ""right"": [""widget:network"", ""quickLaunch"", ""taskList""] } },
  //
  // Shapes and effects (per bar, next to ""layout""):
  //   ""floating"": true, ""margin"": 6   float the bar 6px away from the screen edges
  //   ""cornerRadius"": 10              round the corners
  //   ""spacing"": 4                    gap between items
  //   ""opacity"": 0.85                 background opacity (0..1)
  //   ""blur"": true                    blur behind the bar; only on docked, square bars (Windows 10
  //                                   can't clip blur to a floating/rounded shape, so it's turned off there)
  // ""animations"": { ""enabled"": true, ""durationMs"": 220 },   slide-in, theme cross-fade, widget hover

  // Widget instances by id. ""type"" defaults to the id, so ""widget:cpu"" works with no entry here.
  // Built-in types: clock, cpu, memory, battery, network, command. Any widget can have
  // ""onClick"": { ""action"": ""launch"" | ""run"" | ""command"" | ""theme"" | ""reload"", ""arg"": ""..."" }.
  ""widgets"": {
    // ""clock"":   { ""type"": ""clock"", ""format"": ""ddd d MMM  HH:mm"" },
    // ""battery"": { ""type"": ""battery"", ""hideWhenNoBattery"": false },
    // ""weather"": { ""type"": ""command"", ""command"": ""curl -s wttr.in/?format=3"", ""interval"": 900 },
  },

  // System-wide hotkeys (work whether or not Cairo is the Windows shell). Keys: Win, Ctrl, Alt, Shift + one key.
  // Actions: launch (program/file/URL, optional ""args""), run (command line, no window),
  //          theme (pack name, ""next"" or ""none""), command (built-in Cairo command), reload.
  // A combination already taken by Windows or another program is reported in Settings > Theme Packs.
  ""hotkeys"": [
    // { ""keys"": ""Ctrl+Alt+T"", ""action"": ""theme"",   ""arg"": ""next"" },
    // { ""keys"": ""Ctrl+Alt+Return"", ""action"": ""launch"", ""arg"": ""wt.exe"" },
    // { ""keys"": ""Ctrl+Alt+B"", ""action"": ""run"",     ""arg"": ""C:\\Scripts\\backup.cmd"" },
    // { ""keys"": ""Ctrl+Alt+D"", ""action"": ""command"", ""arg"": ""ToggleDesktopOverlay"" },
    // { ""keys"": ""Ctrl+Alt+R"", ""action"": ""reload"" },
  ],

  // Per-app icon overrides for taskbar and quick launch buttons: exe name -> image file.
  ""appIcons"": {
    // ""notepad.exe"": ""C:\\Icons\\notepad.png"",
  },
}
";
    }
}
