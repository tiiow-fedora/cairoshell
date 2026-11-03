using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>Rules for theme pack folder names (the value of "theme" in cairo-plus.json).</summary>
    public static class ThemeNames
    {
        private const int MaxLength = 64;

        /// <summary>Returns an error message when <paramref name="name"/> can't be used as a pack folder name, otherwise null.</summary>
        public static string Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "a theme name is required.";
            if (name.Length > MaxLength) return $"'{name}' is longer than {MaxLength} characters.";
            if (name != name.Trim()) return "a theme name can't start or end with a space.";
            if (name.StartsWith(".", StringComparison.Ordinal)) return "a theme name can't start with a dot.";

            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' '))
                {
                    return $"'{name}' contains '{c}'; use letters, digits, '-', '_' and spaces only.";
                }
            }

            return null;
        }

        /// <summary>Turns a display name such as "tokyo night!" into a folder name ("TokyoNight").</summary>
        public static string ToFolderName(string display)
        {
            if (string.IsNullOrWhiteSpace(display)) return null;

            var builder = new StringBuilder();
            foreach (string word in display.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = new string(word.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray()).Trim('-', '_');
                if (clean.Length == 0) continue;
                builder.Append(char.ToUpperInvariant(clean[0])).Append(clean.Substring(1));
            }

            string result = builder.ToString();
            if (result.Length > MaxLength) result = result.Substring(0, MaxLength);
            return result.Length == 0 ? null : result;
        }

        /// <summary>A folder name that doesn't collide with an existing folder in <paramref name="parent"/>.</summary>
        public static string MakeUnique(string parent, string name)
        {
            string candidate = name;
            for (int i = 2; Directory.Exists(Path.Combine(parent, candidate)); i++)
            {
                candidate = name + "-" + i;
            }

            return candidate;
        }
    }
}
