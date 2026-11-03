using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CairoDesktop.Customization.Themes
{
    public sealed class ArchiveResult
    {
        /// <summary>The exported zip, or the installed pack folder.</summary>
        public string Path { get; internal set; }

        /// <summary>The installed pack's folder name (install only).</summary>
        public string PackId { get; internal set; }

        public string Error { get; internal set; }

        public bool Success => Error == null;
    }

    /// <summary>
    /// Shares theme packs as .zip files. Installing is defensive: archives are untrusted, so entries that
    /// would land outside the pack folder are refused and the size is capped.
    /// </summary>
    public static class ThemeArchive
    {
        public const long MaxUncompressedBytes = 200L * 1024 * 1024;
        public const int MaxEntries = 2000;

        public static ArchiveResult Export(ThemePack pack, string zipPath)
        {
            try
            {
                string full = System.IO.Path.GetFullPath(zipPath);
                if (File.Exists(full)) File.Delete(full);

                string root = System.IO.Path.GetFullPath(pack.Folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                using (var zip = ZipFile.Open(full, ZipArchiveMode.Create))
                {
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(root.Length).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, pack.Id + "/" + relative, CompressionLevel.Optimal);
                    }
                }

                return new ArchiveResult { Path = full };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ArchiveResult { Error = ex.Message };
            }
        }

        /// <summary>
        /// Installs a pack zip into <paramref name="packsFolder"/>. The archive may hold theme.json at its root or
        /// inside a single top-level folder. An existing pack with the same name is never replaced; the new one gets a "-2" suffix.
        /// </summary>
        public static ArchiveResult Install(string zipPath, string packsFolder)
        {
            string staging = null;
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    string error = Check(zip, out string prefix);
                    if (error != null) return new ArchiveResult { Error = error };

                    Directory.CreateDirectory(packsFolder);
                    staging = System.IO.Path.Combine(packsFolder, ".installing-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(staging);
                    string stagingRoot = System.IO.Path.GetFullPath(staging).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

                    foreach (var entry in zip.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/", StringComparison.Ordinal) || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;

                        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(staging, name.Substring(prefix.Length).Replace('/', System.IO.Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(stagingRoot, StringComparison.Ordinal))
                        {
                            return Fail($"the archive entry '{entry.FullName}' would be written outside the theme folder.", staging);
                        }

                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        using (var input = entry.Open())
                        using (var output = File.Create(target))
                        {
                            input.CopyTo(output);
                        }
                    }

                    var pack = ThemePackLoader.LoadFolder(staging);
                    if (pack.Error != null)
                    {
                        return Fail(pack.Error, staging);
                    }

                    string preferred = ThemeNames.ToFolderName(pack.Manifest.Name) ?? ThemeNames.ToFolderName(prefix.TrimEnd('/')) ?? ThemeNames.ToFolderName(System.IO.Path.GetFileNameWithoutExtension(zipPath));
                    if (preferred == null) return Fail("could not work out a folder name for the theme.", staging);

                    string finalName = ThemeNames.MakeUnique(packsFolder, preferred);
                    string finalFolder = System.IO.Path.Combine(packsFolder, finalName);
                    Directory.Move(staging, finalFolder);
                    staging = null;
                    return new ArchiveResult { Path = finalFolder, PackId = finalName };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return Fail(ex.Message, staging);
            }
        }

        private static ArchiveResult Fail(string message, string staging)
        {
            if (staging != null)
            {
                try { Directory.Delete(staging, true); } catch (Exception) { /* leave it; it is a hidden .installing folder */ }
            }

            return new ArchiveResult { Error = message };
        }

        /// <summary>Finds where theme.json lives and enforces the size limits. Returns an error or null.</summary>
        private static string Check(ZipArchive zip, out string prefix)
        {
            prefix = "";
            if (zip.Entries.Count > MaxEntries) return $"the archive has more than {MaxEntries} files.";
            if (zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes) return "the archive is larger than 200 MB when unpacked.";

            var manifests = new List<string>();
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.Split('/').Any(part => part == "..")) return $"the archive entry '{entry.FullName}' contains '..'.";
                if (name.StartsWith("/", StringComparison.Ordinal) || (name.Length > 1 && name[1] == ':')) return $"the archive entry '{entry.FullName}' has an absolute path.";

                string leaf = name.Substring(name.LastIndexOf('/') + 1);
                if (leaf.Equals(CairoPlusPaths.ManifestFileName, StringComparison.OrdinalIgnoreCase) && name.Count(c => c == '/') <= 1)
                {
                    manifests.Add(name);
                }
            }

            if (manifests.Count == 0) return $"no {CairoPlusPaths.ManifestFileName} at the top level or in a single top-level folder.";

            // Prefer a root-level manifest; otherwise the folder must be unambiguous.
            string root = manifests.FirstOrDefault(m => m.IndexOf('/') < 0);
            if (root != null)
            {
                prefix = "";
                return null;
            }

            if (manifests.Count > 1) return $"the archive holds more than one theme ({string.Join(", ", manifests)}); install them one at a time.";
            prefix = manifests[0].Substring(0, manifests[0].IndexOf('/') + 1);
            return null;
        }
    }
}
