using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Cli
{
    /// <summary>Where the theme commands look and write. Injected so the commands can be tested against temp folders.</summary>
    public sealed class ThemeCliContext
    {
        /// <summary>Theme pack folders, lowest priority first (bundled, then user).</summary>
        public string[] PackFolders { get; set; }

        /// <summary>The folder new, copied and installed packs go to.</summary>
        public string UserPacksFolder { get; set; }

        /// <summary>cairo-plus.json, used only to mark the active pack in --list-themes.</summary>
        public string ConfigFile { get; set; }

        public string CurrentDirectory { get; set; }

        public IEnumerable<string> ExtraWidgetTypes { get; set; }
    }

    /// <summary>
    /// Theme authoring commands that don't need a running Cairo:
    ///   --list-themes
    ///   --validate-theme [name-or-folder]
    ///   --new-theme "Name" [--from Pack]
    ///   --export-theme Pack [file.zip]
    ///   --install-theme file.zip
    /// Each returns a process exit code and writes its report to the supplied writer.
    /// </summary>
    public static class ThemeCli
    {
        public static readonly string[] Commands = { "list-themes", "validate-theme", "new-theme", "export-theme", "install-theme" };

        /// <summary>If <paramref name="args"/> holds one of the theme commands, returns its name (without dashes); otherwise null.</summary>
        public static string FindCommand(string[] args)
        {
            foreach (string arg in args)
            {
                string bare = arg.TrimStart('-', '/');
                if (bare.Length == arg.Length) continue;

                int equals = bare.IndexOf('=');
                string name = equals >= 0 ? bare.Substring(0, equals) : bare;
                string match = Commands.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return null;
        }

        public static int Run(string[] args, ThemeCliContext context, TextWriter output)
        {
            string command = FindCommand(args);
            if (command == null)
            {
                output.WriteLine("No theme command given. Commands: " + string.Join(", ", Commands.Select(c => "--" + c)));
                return 2;
            }

            var positional = Positional(args, command, out string from);

            switch (command)
            {
                case "list-themes": return List(context, output);
                case "validate-theme": return Validate(positional.FirstOrDefault(), context, output);
                case "new-theme": return New(positional.FirstOrDefault(), from, context, output);
                case "export-theme": return Export(positional.FirstOrDefault(), positional.Skip(1).FirstOrDefault(), context, output);
                case "install-theme": return Install(positional.FirstOrDefault(), context, output);
                default: return 2;
            }
        }

        /// <summary>Arguments after the command switch, with "--from X" pulled out. Also accepts "--command=value".</summary>
        private static List<string> Positional(string[] args, string command, out string from)
        {
            from = null;
            var result = new List<string>();
            bool seenCommand = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string bare = arg.TrimStart('-', '/');
                bool isSwitch = bare.Length != arg.Length;

                if (!seenCommand)
                {
                    if (isSwitch && (bare.Equals(command, StringComparison.OrdinalIgnoreCase) || bare.StartsWith(command + "=", StringComparison.OrdinalIgnoreCase)))
                    {
                        seenCommand = true;
                        if (bare.Length > command.Length) result.Add(bare.Substring(command.Length + 1).Trim('"'));
                    }

                    continue;
                }

                if (isSwitch && bare.Equals("from", StringComparison.OrdinalIgnoreCase))
                {
                    from = i + 1 < args.Length ? args[++i] : null;
                }
                else if (isSwitch && bare.StartsWith("from=", StringComparison.OrdinalIgnoreCase))
                {
                    from = bare.Substring(5).Trim('"');
                }
                else if (!isSwitch)
                {
                    result.Add(arg);
                }
            }

            return result;
        }

        private static int List(ThemeCliContext context, TextWriter output)
        {
            var packs = ThemePackLoader.Discover(context.PackFolders);
            string active = ReadActiveTheme(context.ConfigFile);

            if (packs.Count == 0)
            {
                output.WriteLine("No theme packs found in: " + string.Join(", ", context.PackFolders));
                return 0;
            }

            int width = packs.Max(p => p.Id.Length);
            foreach (var pack in packs)
            {
                string marker = string.Equals(pack.Id, active, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                string source = IsUserPack(pack, context) ? "user   " : "bundled";
                string text = pack.Error != null ? "ERROR: " + pack.Error : pack.Manifest.Description;
                output.WriteLine($"{marker} {pack.Id.PadRight(width)}  {source}  {pack.DisplayName}{(string.IsNullOrWhiteSpace(text) ? "" : " - " + text)}");
            }

            output.WriteLine();
            output.WriteLine("* = active. Switch with: CairoDesktop.exe --apply-theme <name>");
            return 0;
        }

        private static bool IsUserPack(ThemePack pack, ThemeCliContext context)
        {
            if (string.IsNullOrEmpty(context.UserPacksFolder)) return false;
            string user = Path.GetFullPath(context.UserPacksFolder).TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(Path.GetFullPath(pack.Folder).TrimEnd(Path.DirectorySeparatorChar));
            return string.Equals(user, parent, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadActiveTheme(string configFile)
        {
            if (string.IsNullOrEmpty(configFile)) return null;
            var result = ConfigLoader.LoadFile(configFile);
            return result.Success && result.Config.Enabled ? result.Config.Theme : null;
        }

        private static int Validate(string target, ThemeCliContext context, TextWriter output)
        {
            var packs = new List<ThemePack>();
            if (string.IsNullOrWhiteSpace(target))
            {
                packs.AddRange(ThemePackLoader.Discover(context.PackFolders));
            }
            else
            {
                var pack = Resolve(target, context);
                if (pack == null)
                {
                    output.WriteLine($"error: no theme pack '{target}' (looked for a folder containing {CairoPlusPaths.ManifestFileName}, and in {string.Join(", ", context.PackFolders)}).");
                    return 2;
                }

                packs.Add(pack);
            }

            var options = new ThemeValidationOptions { ExtraWidgetTypes = context.ExtraWidgetTypes };
            int errors = 0, warnings = 0;

            foreach (var pack in packs)
            {
                var issues = ThemeValidator.Validate(pack, options);
                errors += issues.Count(i => i.Severity == IssueSeverity.Error);
                warnings += issues.Count(i => i.Severity == IssueSeverity.Warning);

                output.WriteLine($"{pack.Id}: {(issues.Count == 0 ? "ok" : issues.Count + " finding(s)")}");
                foreach (var issue in issues.OrderBy(i => i.Severity))
                {
                    output.WriteLine("  " + issue);
                }
            }

            output.WriteLine();
            output.WriteLine($"{packs.Count} pack(s) checked: {errors} error(s), {warnings} warning(s).");
            return errors > 0 ? 1 : 0;
        }

        private static ThemePack Resolve(string target, ThemeCliContext context)
        {
            string path = Path.IsPathRooted(target) || target.StartsWith(".", StringComparison.Ordinal)
                ? Path.GetFullPath(Path.Combine(context.CurrentDirectory ?? Directory.GetCurrentDirectory(), target))
                : null;

            if (path != null && File.Exists(Path.Combine(path, CairoPlusPaths.ManifestFileName)))
            {
                return ThemePackLoader.LoadFolder(path);
            }

            return ThemePackLoader.Find(context.PackFolders, target);
        }

        private static int New(string name, string from, ThemeCliContext context, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                output.WriteLine("error: give the new theme a name, for example: --new-theme \"My Theme\" [--from Nord]");
                return 2;
            }

            ThemePack source = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                source = ThemePackLoader.Find(context.PackFolders, from);
                if (source == null || source.Error != null)
                {
                    output.WriteLine(source == null ? $"error: no theme pack '{from}' to copy." : $"error: '{from}' cannot be copied: {source.Error}");
                    return 2;
                }
            }

            var result = ThemeScaffold.Create(context.UserPacksFolder, name, source);
            if (!result.Success)
            {
                output.WriteLine("error: " + result.Error);
                return 1;
            }

            output.WriteLine("Created " + result.Folder);
            output.WriteLine("Edit theme.json there, then:  CairoDesktop.exe --apply-theme " + Path.GetFileName(result.Folder));
            return 0;
        }

        private static int Export(string name, string zipPath, ThemeCliContext context, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                output.WriteLine("error: say which pack to export, for example: --export-theme Nord [Nord.zip]");
                return 2;
            }

            var pack = Resolve(name, context);
            if (pack == null || pack.Error != null)
            {
                output.WriteLine(pack == null ? $"error: no theme pack '{name}'." : $"error: '{name}' has a broken theme.json: {pack.Error}");
                return 2;
            }

            string target = Path.GetFullPath(Path.Combine(context.CurrentDirectory ?? Directory.GetCurrentDirectory(), string.IsNullOrWhiteSpace(zipPath) ? pack.Id + ".zip" : zipPath));
            var result = ThemeArchive.Export(pack, target);
            if (!result.Success)
            {
                output.WriteLine("error: " + result.Error);
                return 1;
            }

            output.WriteLine("Wrote " + result.Path);
            return 0;
        }

        private static int Install(string zipPath, ThemeCliContext context, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
            {
                output.WriteLine("error: give the theme zip to install, for example: --install-theme Dracula.zip");
                return 2;
            }

            string full = Path.GetFullPath(Path.Combine(context.CurrentDirectory ?? Directory.GetCurrentDirectory(), zipPath));
            if (!File.Exists(full))
            {
                output.WriteLine($"error: '{full}' does not exist.");
                return 2;
            }

            var result = ThemeArchive.Install(full, context.UserPacksFolder);
            if (!result.Success)
            {
                output.WriteLine("error: " + result.Error);
                return 1;
            }

            output.WriteLine($"Installed '{result.PackId}' to {result.Path}");

            var pack = ThemePackLoader.LoadFolder(result.Path);
            if (pack.Manifest?.Hooks != null && !pack.Manifest.Hooks.IsEmpty)
            {
                output.WriteLine("Note: this pack defines hooks (programs that run when it is applied). They stay off unless cairo-plus.json has \"allowPackHooks\": true. Read them in theme.json first.");
            }

            output.WriteLine("Check it with: --validate-theme " + result.PackId + "   Apply it with: --apply-theme " + result.PackId);
            return 0;
        }
    }
}
