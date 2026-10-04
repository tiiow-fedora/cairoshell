using System;
using System.Collections.Generic;
using System.Threading;

namespace CairoDesktop.Customization
{
    /// <summary>
    /// Command-line switches handled before Cairo's single-instance check:
    ///   CairoDesktop.exe --apply-theme "Nord"   activate a theme pack (also enables Cairo Plus)
    ///   CairoDesktop.exe --apply-theme none     turn the theme pack off
    ///   CairoDesktop.exe --reload-config        re-apply cairo-plus.json
    /// The switch edits cairo-plus.json; a running Cairo picks the change up through live reload.
    /// </summary>
    public static class CairoPlusCommandLine
    {
        private const string ApplyTheme = "apply-theme";
        private const string ReloadConfig = "reload-config";

        /// <summary>
        /// Handles Cairo Plus switches and strips them from <paramref name="args"/>.
        /// Returns true if Cairo is already running and this process should exit.
        /// </summary>
        public static bool Handle(ref string[] args, string cairoMutexName)
        {
            var remaining = new List<string>();
            bool handled = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (TryMatch(args, ref i, ApplyTheme, true, out string theme))
                {
                    if (string.Equals(theme, "none", StringComparison.OrdinalIgnoreCase) || theme == "")
                    {
                        theme = null;
                    }

                    CairoPlusService.WriteConfigProperties(theme, setTheme: true, enabled: true);
                    handled = true;
                }
                else if (TryMatch(args, ref i, ReloadConfig, false, out _))
                {
                    TouchConfig();
                    handled = true;
                }
                else
                {
                    remaining.Add(args[i]);
                }
            }

            args = remaining.ToArray();
            return handled && IsCairoRunning(cairoMutexName);
        }

        /// <summary>Matches --name value, --name=value, /name value and /name=value.</summary>
        private static bool TryMatch(string[] args, ref int i, string name, bool takesValue, out string value)
        {
            value = null;
            string arg = args[i];
            string bare = arg.TrimStart('-', '/');
            if (bare.Length == arg.Length) return false;

            if (string.Equals(bare, name, StringComparison.OrdinalIgnoreCase))
            {
                if (takesValue)
                {
                    value = i + 1 < args.Length ? args[++i] : "";
                }

                return true;
            }

            if (bare.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = bare.Substring(name.Length + 1).Trim('"');
                return true;
            }

            return false;
        }

        private static void TouchConfig()
        {
            try
            {
                string path = CairoPlusPaths.ConfigFile;
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                }
            }
            catch
            {
                // Nothing useful to report from a background command-line invocation.
            }
        }

        private static bool IsCairoRunning(string mutexName)
        {
            try
            {
                if (Mutex.TryOpenExisting(mutexName, out Mutex existing))
                {
                    existing.Dispose();
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }

            return false;
        }
    }
}
