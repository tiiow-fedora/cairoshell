using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CairoDesktop.Customization.Config;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>A theme pack folder plus its parsed manifest.</summary>
    public sealed class ThemePack
    {
        /// <summary>Folder name; this is what "theme" in cairo-plus.json refers to.</summary>
        public string Id { get; }

        public string Folder { get; }

        public ThemePackManifest Manifest { get; }

        /// <summary>Manifest parse error, or null.</summary>
        public string Error { get; }

        public string DisplayName => string.IsNullOrWhiteSpace(Manifest?.Name) ? Id : Manifest.Name;

        public ThemePack(string id, string folder, ThemePackManifest manifest, string error)
        {
            Id = id;
            Folder = folder;
            Manifest = manifest;
            Error = error;
        }

        /// <summary>Resolves a pack-relative path; refuses paths that escape the pack folder.</summary>
        public string ResolvePath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            string root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(Folder, relative));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }

    public static class ThemePackLoader
    {
        /// <summary>All packs found in <paramref name="folders"/>, sorted by display name. Later folders override earlier ones.</summary>
        public static List<ThemePack> Discover(IEnumerable<string> folders)
        {
            var packs = new Dictionary<string, ThemePack>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string dir in Directory.GetDirectories(folder))
                {
                    if (File.Exists(Path.Combine(dir, CairoPlusPaths.ManifestFileName)))
                    {
                        var pack = LoadFolder(dir);
                        packs[pack.Id] = pack;
                    }
                }
            }

            return packs.Values.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static ThemePack Find(IEnumerable<string> folders, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            ThemePack found = null;
            foreach (string folder in folders)
            {
                string dir = Path.Combine(folder, id);
                if (File.Exists(Path.Combine(dir, CairoPlusPaths.ManifestFileName)))
                {
                    found = LoadFolder(dir);
                }
            }

            return found;
        }

        public static ThemePack LoadFolder(string dir)
        {
            string id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string manifestPath = Path.Combine(dir, CairoPlusPaths.ManifestFileName);

            try
            {
                var manifest = ConfigLoader.Deserialize<ThemePackManifest>(ConfigLoader.ReadAllTextShared(manifestPath)) ?? new ThemePackManifest();
                return new ThemePack(id, dir, manifest, null);
            }
            catch (JsonException ex)
            {
                return new ThemePack(id, dir, null, $"{CairoPlusPaths.ManifestFileName} {ConfigLoader.DescribeJsonError(ex)}");
            }
            catch (Exception ex)
            {
                return new ThemePack(id, dir, null, ex.Message);
            }
        }
    }
}
