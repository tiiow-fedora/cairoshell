using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Settings
{
    /// <summary>The small manifest stored as manifest.json in a settings bundle.</summary>
    public sealed class BundleManifest
    {
        public const string FormatName = "cairo-plus-settings";

        /// <summary>Bundle format version written by this build. Bundles with a higher version are refused.</summary>
        public const int CurrentVersion = 1;

        public string Format { get; set; }

        public int Version { get; set; }

        public string CreatedUtc { get; set; }

        /// <summary>The "theme" value of the exported config, for display only.</summary>
        public string Theme { get; set; }

        /// <summary>Names of the user theme packs inside the bundle, for display only (the folders are authoritative).</summary>
        public List<string> Packs { get; set; }
    }

    /// <summary>What a bundle contains, read without touching the disk.</summary>
    public sealed class BundleInfo
    {
        public BundleManifest Manifest { get; internal set; }

        /// <summary>The bundled cairo-plus.json text, comments included.</summary>
        public string ConfigText { get; internal set; }

        public CairoPlusConfig Config { get; internal set; }

        /// <summary>Folder names of the theme packs in the bundle.</summary>
        public List<string> Packs { get; internal set; } = new List<string>();

        /// <summary>Config sections that can run programs on import: hooks, hotkeys, widgets, allowPackHooks.</summary>
        public List<string> ExecutableSections { get; internal set; } = new List<string>();

        /// <summary>Mistakes <see cref="ConfigChecker"/> found in the bundled config.</summary>
        public List<string> ConfigProblems { get; internal set; } = new List<string>();
    }

    public sealed class BundleResult
    {
        /// <summary>The exported zip (export only).</summary>
        public string Path { get; internal set; }

        public string Error { get; internal set; }

        public bool Success => Error == null;

        public BundleInfo Info { get; internal set; }

        /// <summary>Config sections that were removed because executable content wasn't trusted.</summary>
        public List<string> Dropped { get; internal set; } = new List<string>();

        /// <summary>Packs copied into the user themes folder, by their final folder name.</summary>
        public List<string> InstalledPacks { get; internal set; } = new List<string>();

        /// <summary>Packs that were already installed with identical contents.</summary>
        public List<string> SkippedPacks { get; internal set; } = new List<string>();

        /// <summary>Packs whose name was taken by a different pack, bundle name to the name used instead.</summary>
        public Dictionary<string, string> RenamedPacks { get; internal set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where the previous cairo-plus.json was copied, or null if there was none.</summary>
        public string BackupPath { get; internal set; }

        public List<string> Warnings { get; internal set; } = new List<string>();
    }

    public sealed class ExportOptions
    {
        /// <summary>Include the user theme packs (bundled packs are never included; every install has them).</summary>
        public bool IncludePacks { get; set; } = true;

        /// <summary>Override the time stamped into the manifest (tests).</summary>
        public DateTime? Now { get; set; }
    }

    public sealed class ImportOptions
    {
        /// <summary>
        /// Keep hooks, hotkeys that launch programs, "command" widgets and "allowPackHooks" from the bundle. Off by
        /// default: a settings file from someone else should not be able to run programs on this PC.
        /// </summary>
        public bool TrustExecutable { get; set; }

        public bool ImportPacks { get; set; } = true;

        /// <summary>Work out and report what would happen without writing anything.</summary>
        public bool DryRun { get; set; }

        /// <summary>Folders searched (besides the user folder) to tell whether the bundle's theme exists, e.g. the bundled packs.</summary>
        public IEnumerable<string> OtherPackFolders { get; set; }

        public DateTime? Now { get; set; }
    }

    /// <summary>
    /// Exports cairo-plus.json plus the user's own theme packs as one .zip, and imports such a zip. Importing is
    /// defensive: the archive is untrusted, the existing config is backed up, existing packs are never overwritten
    /// and code-running sections are dropped unless asked for.
    /// </summary>
    public static class SettingsBundle
    {
        public const string ManifestEntry = "manifest.json";
        public const string ConfigEntry = CairoPlusPaths.ConfigFileName;
        public const string PacksPrefix = CairoPlusPaths.ThemePacksFolderName + "/";

        public const long MaxUncompressedBytes = 200L * 1024 * 1024;
        public const int MaxEntries = 2000;
        public const int MaxTextBytes = 1024 * 1024;

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // ---------------------------------------------------------------- export

        public static BundleResult Export(string configFile, string userPacksFolder, string zipPath, ExportOptions options = null)
        {
            options = options ?? new ExportOptions();

            if (string.IsNullOrEmpty(configFile) || !File.Exists(configFile))
            {
                return Fail("there is no " + CairoPlusPaths.ConfigFileName + " to export.");
            }

            string configText;
            try { configText = ConfigLoader.ReadAllTextShared(configFile); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Fail(ex.Message); }

            var parsed = ConfigLoader.Parse(configText);
            if (!parsed.Success)
            {
                return Fail($"{CairoPlusPaths.ConfigFileName} has a syntax error ({parsed.Error}); fix it before exporting.");
            }

            var packFolders = new List<string>();
            if (options.IncludePacks && !string.IsNullOrEmpty(userPacksFolder) && Directory.Exists(userPacksFolder))
            {
                foreach (string dir in Directory.GetDirectories(userPacksFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (name.StartsWith(".", StringComparison.Ordinal) || ThemeNames.Validate(name) != null) continue;
                    if (!File.Exists(System.IO.Path.Combine(dir, CairoPlusPaths.ManifestFileName))) continue;
                    packFolders.Add(dir);
                }
            }

            var manifest = new BundleManifest
            {
                Format = BundleManifest.FormatName,
                Version = BundleManifest.CurrentVersion,
                CreatedUtc = (options.Now ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                Theme = parsed.Config.Theme,
                Packs = packFolders.Select(System.IO.Path.GetFileName).ToList()
            };

            try
            {
                string full = System.IO.Path.GetFullPath(zipPath);
                if (File.Exists(full)) File.Delete(full);

                using (var zip = ZipFile.Open(full, ZipArchiveMode.Create))
                {
                    WriteText(zip, ManifestEntry, JsonSerializer.Serialize(manifest, WriteOptions));
                    WriteText(zip, ConfigEntry, configText);

                    foreach (string dir in packFolders)
                    {
                        string root = System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                        {
                            string relative = file.Substring(root.Length).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, PacksPrefix + System.IO.Path.GetFileName(dir) + "/" + relative, CompressionLevel.Optimal);
                        }
                    }
                }

                var result = new BundleResult { Path = full };
                var exec = FindExecutableSections(parsed.Config);
                if (exec.Count > 0)
                {
                    result.Warnings.Add("The config contains " + string.Join(", ", exec) + ". Whoever imports this bundle will have those removed unless they choose to trust it.");
                }

                return result;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail(ex.Message);
            }
        }

        // ---------------------------------------------------------------- inspect

        /// <summary>Reads and checks a bundle without extracting it. <see cref="BundleResult.Info"/> is set on success.</summary>
        public static BundleResult Analyze(string zipPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    return Analyze(zip);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return Fail(ex.Message);
            }
        }

        private static BundleResult Analyze(ZipArchive zip)
        {
            string error = CheckEntries(zip);
            if (error != null) return Fail(error);

            var manifestEntry = Find(zip, ManifestEntry);
            var configEntry = Find(zip, ConfigEntry);
            if (manifestEntry == null) return Fail("this is not a Cairo Plus settings bundle (no " + ManifestEntry + ").");
            if (configEntry == null) return Fail("the bundle has no " + ConfigEntry + ".");

            BundleManifest manifest;
            try
            {
                manifest = ConfigLoader.Deserialize<BundleManifest>(ReadText(manifestEntry));
            }
            catch (JsonException ex)
            {
                return Fail(ManifestEntry + " is not valid JSON (" + ConfigLoader.DescribeJsonError(ex) + ").");
            }

            if (manifest == null || !string.Equals(manifest.Format, BundleManifest.FormatName, StringComparison.Ordinal))
            {
                return Fail("this is not a Cairo Plus settings bundle (wrong format marker).");
            }

            if (manifest.Version < 1) return Fail($"the bundle version {manifest.Version} is not valid.");
            if (manifest.Version > BundleManifest.CurrentVersion)
            {
                return Fail($"the bundle was made by a newer Cairo Plus (bundle version {manifest.Version}, this build reads up to {BundleManifest.CurrentVersion}). Update Cairo Plus and try again.");
            }

            string configText = ReadText(configEntry);
            var parsed = ConfigLoader.Parse(configText);
            if (!parsed.Success) return Fail($"the bundled {ConfigEntry} has a syntax error ({parsed.Error}).");

            var info = new BundleInfo
            {
                Manifest = manifest,
                ConfigText = configText,
                Config = parsed.Config,
                ConfigProblems = ConfigChecker.Check(parsed.Config),
                ExecutableSections = FindExecutableSections(parsed.Config)
            };

            var packs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                string name = Normalize(entry.FullName);
                if (!name.StartsWith(PacksPrefix, StringComparison.Ordinal)) continue;

                string[] parts = name.Substring(PacksPrefix.Length).Split('/');
                if (parts.Length < 2 || parts[0].Length == 0) continue;

                string nameError = ThemeNames.Validate(parts[0]);
                if (nameError != null) return Fail($"the bundle holds a theme folder with an unusable name: {nameError}");

                bool hasManifest = parts.Length == 2 && parts[1].Equals(CairoPlusPaths.ManifestFileName, StringComparison.OrdinalIgnoreCase);
                packs[parts[0]] = (packs.TryGetValue(parts[0], out bool had) && had) || hasManifest;
            }

            foreach (var pack in packs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!pack.Value) return Fail($"the theme folder '{pack.Key}' in the bundle has no {CairoPlusPaths.ManifestFileName}.");
                info.Packs.Add(pack.Key);
            }

            return new BundleResult { Info = info };
        }

        /// <summary>
        /// Names of the config parts that can run programs: "hooks", "hotkeys" (launch or run actions),
        /// "widgets" (a "command" widget) and "allowPackHooks".
        /// </summary>
        public static List<string> FindExecutableSections(CairoPlusConfig config)
        {
            var sections = new List<string>();
            if (config == null) return sections;

            if (config.Hooks != null && !config.Hooks.IsEmpty) sections.Add("hooks");

            if (config.Hotkeys != null && config.Hotkeys.Any(h => h != null && IsProgramAction(h.Action))) sections.Add("hotkeys");

            if (config.Widgets != null && config.Widgets.Any(w => IsCommandWidget(w.Key, w.Value))) sections.Add("widgets");

            if (config.AllowPackHooks) sections.Add("allowPackHooks");

            return sections;
        }

        private static bool IsProgramAction(string action)
        {
            string a = (action ?? "").Trim();
            return a.Equals("launch", StringComparison.OrdinalIgnoreCase) || a.Equals("run", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCommandWidget(string id, JsonElement options)
        {
            string type = id;
            if (options.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in options.EnumerateObject())
                {
                    if (property.NameEquals("type") || property.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String) type = property.Value.GetString();
                        break;
                    }
                }
            }

            return string.Equals(type, "command", StringComparison.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- import

        public static BundleResult Import(string zipPath, string configFile, string userPacksFolder, ImportOptions options = null)
        {
            options = options ?? new ImportOptions();

            string staging = null;
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var analysis = Analyze(zip);
                    if (!analysis.Success) return analysis;

                    var info = analysis.Info;
                    var result = new BundleResult { Info = info };
                    result.Warnings.AddRange(info.ConfigProblems.Select(p => "bundled config: " + p));

                    // 1. Decide what stays in the config.
                    string configText = info.ConfigText;
                    if (!options.TrustExecutable)
                    {
                        foreach (string section in info.ExecutableSections)
                        {
                            configText = section == "allowPackHooks"
                                ? JsonTextEditor.SetBool(configText, "allowPackHooks", false)
                                : JsonTextEditor.SetRaw(configText, section, "null");
                            result.Dropped.Add(section);
                        }
                    }
                    else if (info.ExecutableSections.Count > 0)
                    {
                        result.Warnings.Add("Trusted: " + string.Join(", ", info.ExecutableSections) + " can run programs on this PC.");
                    }

                    // 2. Plan the packs.
                    var wanted = options.ImportPacks ? info.Packs : new List<string>();
                    var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // bundle name -> final name; null = identical, skip
                    if (wanted.Count > 0)
                    {
                        staging = System.IO.Path.Combine(userPacksFolder, ".importing-" + Guid.NewGuid().ToString("N"));
                        if (!options.DryRun)
                        {
                            Directory.CreateDirectory(staging);
                            Extract(zip, staging);
                        }
                    }

                    var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string name in wanted)
                    {
                        string existing = System.IO.Path.Combine(userPacksFolder, name);
                        if (!Directory.Exists(existing))
                        {
                            plan[name] = name;
                            taken.Add(name);
                        }
                        else if (!options.DryRun && SameContents(System.IO.Path.Combine(staging, name), existing))
                        {
                            plan[name] = null;
                        }
                        else
                        {
                            string unique = name;
                            for (int i = 2; Directory.Exists(System.IO.Path.Combine(userPacksFolder, unique)) || taken.Contains(unique); i++) unique = name + "-" + i;
                            plan[name] = unique;
                            taken.Add(unique);
                            result.RenamedPacks[name] = unique;
                        }
                    }

                    // 3. The config's theme must follow a rename.
                    string theme = info.Config.Theme;
                    if (theme != null && result.RenamedPacks.TryGetValue(theme, out string newTheme))
                    {
                        configText = JsonTextEditor.SetString(configText, "theme", newTheme);
                        theme = newTheme;
                    }

                    if (!string.IsNullOrWhiteSpace(theme) && !plan.ContainsKey(theme) && !PackExists(theme, userPacksFolder, options.OtherPackFolders))
                    {
                        result.Warnings.Add($"the theme '{theme}' is not installed here and is not in the bundle; no theme will be applied until you install it.");
                    }

                    if (info.Config.AppIcons != null && info.Config.AppIcons.Values.Any(v => v != null && System.IO.Path.IsPathRooted(v)))
                    {
                        result.Warnings.Add("\"appIcons\" uses absolute paths, which probably don't exist on this PC.");
                    }

                    foreach (var entry in plan)
                    {
                        if (entry.Value == null) result.SkippedPacks.Add(entry.Key);
                        else result.InstalledPacks.Add(entry.Value);
                    }

                    if (options.DryRun) return result;

                    // 4. Install packs first so a live reload finds them, then swap in the config.
                    foreach (var entry in plan.Where(p => p.Value != null))
                    {
                        Directory.Move(System.IO.Path.Combine(staging, entry.Key), System.IO.Path.Combine(userPacksFolder, entry.Value));
                    }

                    if (File.Exists(configFile))
                    {
                        string stamp = (options.Now ?? DateTime.Now).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                        result.BackupPath = BackupName(configFile, stamp);
                        File.Copy(configFile, result.BackupPath, false);
                    }

                    WriteAtomically(configFile, configText);
                    return result;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, true); } catch (Exception) { /* hidden .importing folder; harmless */ }
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static BundleResult Fail(string message) => new BundleResult { Error = message };

        private static string Normalize(string entryName) => entryName.Replace('\\', '/');

        private static ZipArchiveEntry Find(ZipArchive zip, string name)
        {
            return zip.Entries.FirstOrDefault(e => string.Equals(Normalize(e.FullName), name, StringComparison.Ordinal));
        }

        /// <summary>Limits and path checks over every entry. Returns an error or null.</summary>
        private static string CheckEntries(ZipArchive zip)
        {
            if (zip.Entries.Count > MaxEntries) return $"the bundle has more than {MaxEntries} files.";
            if (zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes) return "the bundle is larger than 200 MB when unpacked.";

            foreach (var entry in zip.Entries)
            {
                string name = Normalize(entry.FullName);
                if (name.Split('/').Any(part => part == "..")) return $"the entry '{entry.FullName}' contains '..'.";
                if (name.StartsWith("/", StringComparison.Ordinal) || (name.Length > 1 && name[1] == ':')) return $"the entry '{entry.FullName}' has an absolute path.";
                if (name.EndsWith("/", StringComparison.Ordinal)) continue;

                bool allowed = name == ManifestEntry || name == ConfigEntry || name.StartsWith(PacksPrefix, StringComparison.Ordinal);
                if (!allowed) return $"the entry '{entry.FullName}' does not belong in a settings bundle.";
                if ((name == ManifestEntry || name == ConfigEntry) && entry.Length > MaxTextBytes) return $"'{entry.FullName}' is larger than 1 MB.";
            }

            return null;
        }

        private static string ReadText(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var buffer = new char[4096];
                var builder = new StringBuilder();
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    builder.Append(buffer, 0, read);
                    if (builder.Length > MaxTextBytes) throw new InvalidDataException($"'{entry.FullName}' is larger than 1 MB.");
                }

                return builder.ToString();
            }
        }

        private static void WriteText(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        /// <summary>Extracts ThemePacks/* into <paramref name="staging"/>, so staging\&lt;pack&gt;\theme.json. Caps total bytes actually written.</summary>
        private static void Extract(ZipArchive zip, string staging)
        {
            string stagingRoot = System.IO.Path.GetFullPath(staging).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            long total = 0;

            foreach (var entry in zip.Entries)
            {
                string name = Normalize(entry.FullName);
                if (name.EndsWith("/", StringComparison.Ordinal) || !name.StartsWith(PacksPrefix, StringComparison.Ordinal)) continue;

                string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(staging, name.Substring(PacksPrefix.Length).Replace('/', System.IO.Path.DirectorySeparatorChar)));
                if (!target.StartsWith(stagingRoot, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"the entry '{entry.FullName}' would be written outside the theme folder.");
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                using (var input = entry.Open())
                using (var output = File.Create(target))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > MaxUncompressedBytes) throw new InvalidDataException("the bundle is larger than 200 MB when unpacked.");
                        output.Write(buffer, 0, read);
                    }
                }
            }
        }

        private static bool SameContents(string a, string b)
        {
            if (!Directory.Exists(a) || !Directory.Exists(b)) return false;

            string[] filesA = RelativeFiles(a);
            string[] filesB = RelativeFiles(b);
            if (!filesA.SequenceEqual(filesB, StringComparer.OrdinalIgnoreCase)) return false;

            foreach (string relative in filesA)
            {
                string fa = System.IO.Path.Combine(a, relative);
                string fb = System.IO.Path.Combine(b, relative);
                if (new FileInfo(fa).Length != new FileInfo(fb).Length) return false;
                if (!File.ReadAllBytes(fa).SequenceEqual(File.ReadAllBytes(fb))) return false;
            }

            return true;
        }

        private static string[] RelativeFiles(string root)
        {
            string prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            return Directory.GetFiles(prefix, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(prefix.Length).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool PackExists(string theme, string userPacksFolder, IEnumerable<string> others)
        {
            var folders = new List<string> { userPacksFolder };
            if (others != null) folders.AddRange(others);
            return folders.Any(f => !string.IsNullOrEmpty(f) && File.Exists(System.IO.Path.Combine(f, theme, CairoPlusPaths.ManifestFileName)));
        }

        private static string BackupName(string configFile, string stamp)
        {
            string candidate = configFile + ".bak-" + stamp;
            for (int i = 2; File.Exists(candidate); i++) candidate = configFile + ".bak-" + stamp + "-" + i;
            return candidate;
        }

        /// <summary>Writes a temp file next to the target and moves it over, so a live reload never reads half a file.</summary>
        private static void WriteAtomically(string path, string text)
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            Directory.CreateDirectory(directory);

            string temp = System.IO.Path.Combine(directory, "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, text, new UTF8Encoding(false));

            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception)
            {
                try { File.Delete(temp); } catch (Exception) { /* best effort */ }
                throw;
            }
        }
    }
}
