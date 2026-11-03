using System;
using System.IO;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    public sealed class ScaffoldResult
    {
        public string Folder { get; internal set; }

        /// <summary>Null on success.</summary>
        public string Error { get; internal set; }

        public bool Success => Error == null;
    }

    /// <summary>Creates a new theme pack folder, either from a commented template or as a copy of an existing pack.</summary>
    public static class ThemeScaffold
    {
        /// <param name="packsFolder">Where packs live, normally %LOCALAPPDATA%\Cairo Desktop\ThemePacks.</param>
        /// <param name="name">Display name; also used for the folder name (see <see cref="ThemeNames"/>).</param>
        /// <param name="copyFrom">An existing pack to copy, or null for the template.</param>
        public static ScaffoldResult Create(string packsFolder, string name, ThemePack copyFrom = null)
        {
            string nameError = ThemeNames.Validate(name);
            if (nameError != null) return new ScaffoldResult { Error = nameError };

            string folderName = ThemeNames.ToFolderName(name);
            string folder = Path.Combine(packsFolder, folderName);
            if (Directory.Exists(folder))
            {
                return new ScaffoldResult { Error = $"a theme pack named '{folderName}' already exists in {packsFolder}." };
            }

            try
            {
                if (copyFrom != null)
                {
                    CopyDirectory(copyFrom.Folder, folder);
                    string manifestPath = Path.Combine(folder, CairoPlusPaths.ManifestFileName);
                    string text = ConfigLoader.ReadAllTextShared(manifestPath);
                    text = JsonTextEditor.SetString(text, "name", name);
                    File.WriteAllText(manifestPath, text);
                }
                else
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, CairoPlusPaths.ManifestFileName), Template(name));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                return new ScaffoldResult { Error = ex.Message };
            }

            return new ScaffoldResult { Folder = folder };
        }

        internal static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        public static string Template(string name)
        {
            return @"// Cairo Plus theme pack. Comments and trailing commas are allowed.
// Edit and save: if this pack is active, the desktop updates live.
// Check your work with:  CairoDesktop.exe --validate-theme """ + name.Replace("\"", "") + @"""
{
  ""name"": " + System.Text.Json.JsonSerializer.Serialize(name) + @",
  ""author"": """",
  ""description"": """",

  // Cairo XAML theme underneath: Default, Flat, Flat Accent, White, Fade, MiniMeow.
  ""baseTheme"": ""Default"",
  ""darkMode"": true,

  // Nine colour roles. Only ""background"" is needed; the rest are derived when left out.
  // Colours are #RGB, #RRGGBB or #AARRGGBB.
  ""palette"": {
    ""background"": ""#1E1E2E"",
    ""surface"":    ""#2A2A3C"",
    ""overlay"":    ""#38384D"",
    ""border"":     ""#45475A"",
    ""text"":       ""#E6E6F0"",
    ""subtext"":    ""#A6A8BA"",
    ""accent"":     ""#7AA2F7"",
    ""accentText"": ""#1E1E2E"",
    ""urgent"":     ""#F7768E"",
  },

  // Override any single Cairo colour by its key in Cairo Desktop/Themes/Cairo.xaml.
  // ""resources"": { ""MenuTopBorderBrush"": ""#FF79C6"" },

  // ""font"": { ""family"": ""Segoe UI"", ""size"": 12 },
  // ""font"": { ""family"": ""JetBrains Mono"", ""file"": ""fonts/JetBrainsMono.ttf"", ""size"": 12 },

  // Put wallpaper.jpg next to this file, then uncomment. Styles: fill, fit, stretch, center, tile, span.
  // ""wallpaper"": { ""file"": ""wallpaper.jpg"", ""style"": ""fill"" },

  // Cairo's own icons are replaced by icons/<Key>.png automatically (MenuIcon, SearchIcon, ...).
  // ""appIcons"": { ""notepad.exe"": ""icons/apps/notepad.png"" },

  // Shapes and layout, per bar (see CAIRO-PLUS.md).
  // ""menuBar"": { ""cornerRadius"": 8, ""opacity"": 0.95, ""layout"": { ""center"": [""widget:clock""] } },
  // ""taskbar"": { ""floating"": true, ""margin"": 6, ""cornerRadius"": 10 },
  // ""animations"": { ""enabled"": true },
  // ""widgets"": { ""clock"": { ""type"": ""clock"", ""format"": ""ddd d MMM  HH:mm"" } },

  // Programs to run when this pack is applied. They only run if cairo-plus.json has ""allowPackHooks"": true.
  // The script sees CAIRO_THEME, CAIRO_THEME_DIR and CAIRO_COLOR_* in its environment.
  // ""hooks"": { ""onThemeApplied"": [ { ""action"": ""run"", ""arg"": ""powershell -NoProfile -File \""%CAIRO_THEME_DIR%\\apply.ps1\"""" } ] },
}
";
        }
    }
}
