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

  // Per-app icon overrides for taskbar and quick launch buttons: exe name -> image file.
  ""appIcons"": {
    // ""notepad.exe"": ""C:\\Icons\\notepad.png"",
  },
}
";
    }
}
