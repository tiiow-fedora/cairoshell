using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CairoDesktop.Customization.Settings;

namespace CairoDesktop.Customization.Cli
{
    /// <summary>
    /// Settings bundle commands that don't need a running Cairo:
    ///   --export-settings [file.zip] [--no-packs]
    ///   --import-settings file.zip [--dry-run] [--no-packs] [--trust-code]
    /// Uses the same <see cref="ThemeCliContext"/> as the theme commands.
    /// </summary>
    public static class SettingsCli
    {
        public static readonly string[] Commands = { "export-settings", "import-settings" };

        private const string DefaultBundleName = "cairo-plus-settings.zip";

        /// <summary>If <paramref name="args"/> holds one of the settings commands, returns its name (without dashes); otherwise null.</summary>
        public static string FindCommand(string[] args)
        {
            foreach (string arg in args)
            {
                if (!IsSwitch(arg)) continue;
                string bare = arg.TrimStart('-', '/');

                int equals = bare.IndexOf('=');
                string name = equals >= 0 ? bare.Substring(0, equals) : bare;
                string match = Commands.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return null;
        }

        /// <summary>"--x" and "/x" are switches; "/tmp/file.zip" is a path.</summary>
        private static bool IsSwitch(string arg)
        {
            return arg.StartsWith("-", StringComparison.Ordinal) || (arg.StartsWith("/", StringComparison.Ordinal) && arg.IndexOfAny(new[] { '/', '\\' }, 1) < 0);
        }

        public static int Run(string[] args, ThemeCliContext context, TextWriter output)
        {
            string command = FindCommand(args);
            if (command == null)
            {
                output.WriteLine("No settings command given. Commands: " + string.Join(", ", Commands.Select(c => "--" + c)));
                return 2;
            }

            var positional = Parse(args, command, out bool noPacks, out bool dryRun, out bool trust);
            string file = positional.FirstOrDefault();

            return command == "export-settings"
                ? Export(file, !noPacks, context, output)
                : Import(file, new ImportOptions { ImportPacks = !noPacks, DryRun = dryRun, TrustExecutable = trust, OtherPackFolders = context.PackFolders }, context, output);
        }

        private static List<string> Parse(string[] args, string command, out bool noPacks, out bool dryRun, out bool trust)
        {
            noPacks = dryRun = trust = false;
            var result = new List<string>();
            bool seenCommand = false;

            foreach (string arg in args)
            {
                string bare = arg.TrimStart('-', '/');
                bool isSwitch = IsSwitch(arg);

                if (!seenCommand)
                {
                    if (isSwitch && (bare.Equals(command, StringComparison.OrdinalIgnoreCase) || bare.StartsWith(command + "=", StringComparison.OrdinalIgnoreCase)))
                    {
                        seenCommand = true;
                        if (bare.Length > command.Length) result.Add(bare.Substring(command.Length + 1).Trim('"'));
                    }

                    continue;
                }

                if (!isSwitch) result.Add(arg);
                else if (bare.Equals("no-packs", StringComparison.OrdinalIgnoreCase)) noPacks = true;
                else if (bare.Equals("dry-run", StringComparison.OrdinalIgnoreCase)) dryRun = true;
                else if (bare.Equals("trust-code", StringComparison.OrdinalIgnoreCase)) trust = true;
            }

            return result;
        }

        private static string FullPath(string path, ThemeCliContext context)
        {
            return Path.GetFullPath(Path.Combine(context.CurrentDirectory ?? Directory.GetCurrentDirectory(), path));
        }

        private static int Export(string file, bool includePacks, ThemeCliContext context, TextWriter output)
        {
            string target = FullPath(string.IsNullOrWhiteSpace(file) ? DefaultBundleName : file, context);
            var result = SettingsBundle.Export(context.ConfigFile, context.UserPacksFolder, target, new ExportOptions { IncludePacks = includePacks });
            if (!result.Success)
            {
                output.WriteLine("error: " + result.Error);
                return 1;
            }

            output.WriteLine("Wrote " + result.Path);
            foreach (string warning in result.Warnings) output.WriteLine("Note: " + warning);
            return 0;
        }

        private static int Import(string file, ImportOptions options, ThemeCliContext context, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                output.WriteLine("error: give the bundle to import, for example: --import-settings " + DefaultBundleName + " [--dry-run] [--no-packs] [--trust-code]");
                return 2;
            }

            string full = FullPath(file, context);
            if (!File.Exists(full))
            {
                output.WriteLine($"error: '{full}' does not exist.");
                return 2;
            }

            var result = SettingsBundle.Import(full, context.ConfigFile, context.UserPacksFolder, options);
            if (!result.Success)
            {
                output.WriteLine("error: " + result.Error);
                return 1;
            }

            string verb = options.DryRun ? "Would " : "";
            output.WriteLine(options.DryRun ? "Dry run: nothing was changed." : "Imported settings into " + context.ConfigFile);
            if (result.BackupPath != null) output.WriteLine("Previous settings saved as " + result.BackupPath);
            foreach (string pack in result.InstalledPacks) output.WriteLine($"{verb}{(options.DryRun ? "install" : "Installed")} theme pack '{pack}'");
            foreach (string pack in result.SkippedPacks) output.WriteLine($"Theme pack '{pack}' is already installed.");
            foreach (var rename in result.RenamedPacks) output.WriteLine($"'{rename.Key}' already exists here with different contents; the imported one is '{rename.Value}'.");
            foreach (string section in result.Dropped) output.WriteLine($"{(options.DryRun ? "Would remove" : "Removed")} \"{section}\" (it can run programs). Review the bundle, then use --trust-code to keep it.");
            foreach (string warning in result.Warnings) output.WriteLine("Note: " + warning);
            return 0;
        }
    }
}
