using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CairoDesktop.Customization.Cli;
using Microsoft.Win32.SafeHandles;

namespace CairoDesktop.Customization
{
    /// <summary>
    /// Command-line switches handled before Cairo's single-instance check:
    ///   CairoDesktop.exe --apply-theme "Nord"   activate a theme pack (also enables Cairo Plus)
    ///   CairoDesktop.exe --apply-theme none     turn the theme pack off
    ///   CairoDesktop.exe --reload-config        re-apply cairo-plus.json
    /// The switch edits cairo-plus.json; a running Cairo picks the change up through live reload.
    ///
    /// Theme authoring commands (see <see cref="ThemeCli"/>) run to completion and exit without starting Cairo:
    ///   --list-themes, --validate-theme [pack], --new-theme "Name" [--from Pack], --export-theme Pack [file.zip], --install-theme file.zip
    /// </summary>
    public static class CairoPlusCommandLine
    {
        /// <summary>Exit code for the process when <see cref="Handle"/> returned true.</summary>
        public static int ExitCode { get; private set; }

        private const string ApplyTheme = "apply-theme";
        private const string ReloadConfig = "reload-config";

        /// <summary>
        /// Handles Cairo Plus switches and strips them from <paramref name="args"/>.
        /// Returns true if Cairo is already running and this process should exit.
        /// </summary>
        public static bool Handle(ref string[] args, string cairoMutexName)
        {
            if (ThemeCli.FindCommand(args) != null || SettingsCli.FindCommand(args) != null)
            {
                ExitCode = RunThemeCommand(args);
                return true;
            }

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

        private static int RunThemeCommand(string[] args)
        {
            var context = new ThemeCliContext
            {
                PackFolders = CairoPlusPaths.ThemePackFolders,
                UserPacksFolder = CairoPlusPaths.UserThemePacksFolder,
                ConfigFile = CairoPlusPaths.ConfigFile,
                CurrentDirectory = Directory.GetCurrentDirectory()
            };

            var buffer = new StringWriter();
            int code;
            try
            {
                code = ThemeCli.FindCommand(args) != null ? ThemeCli.Run(args, context, buffer) : SettingsCli.Run(args, context, buffer);
            }
            catch (Exception ex)
            {
                buffer.WriteLine("error: " + ex.Message);
                code = 1;
            }

            Show(buffer.ToString());
            return code;
        }

        /// <summary>
        /// CairoDesktop.exe is a windowed program, so it has no console of its own. Print to the console that
        /// started it (or to a redirected stdout); if there is none, show the text in a message box.
        /// </summary>
        private static void Show(string text)
        {
            try
            {
                IntPtr stdout = GetStdHandle(-11);
                if (stdout != IntPtr.Zero && stdout != new IntPtr(-1))
                {
                    Console.Out.Write(text);
                    Console.Out.Flush();
                    return;
                }

                if (AttachConsole(-1))
                {
                    IntPtr conout = CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (conout != new IntPtr(-1))
                    {
                        using (var stream = new FileStream(new SafeFileHandle(conout, true), FileAccess.Write))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            writer.Write("\r\n" + text);
                            writer.Flush();
                        }

                        return;
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the message box
            }

            System.Windows.Forms.MessageBox.Show(text, "Cairo Plus");
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

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
