using System;
using System.IO;

namespace CairoDesktop.Customization.Tests
{
    /// <summary>Finds files in the repository from wherever the tests run.</summary>
    internal static class TestPaths
    {
        /// <summary>"Cairo Desktop" solution folder (contains CairoDesktop.Customization). Honours CAIRO_REPO_ROOT.</summary>
        public static string SolutionFolder
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CAIRO_REPO_ROOT");
                var dir = new DirectoryInfo(string.IsNullOrEmpty(env) ? AppContext.BaseDirectory : env);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "CairoDesktop.Customization", "ThemePacks")))
                    {
                        return dir.FullName;
                    }

                    string nested = Path.Combine(dir.FullName, "Cairo Desktop");
                    if (Directory.Exists(Path.Combine(nested, "CairoDesktop.Customization", "ThemePacks")))
                    {
                        return nested;
                    }

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not find the Cairo Desktop solution folder from " + AppContext.BaseDirectory);
            }
        }

        public static string BundledPacks => Path.Combine(SolutionFolder, "CairoDesktop.Customization", "ThemePacks");

        public static string CairoXaml => Path.Combine(SolutionFolder, "Cairo Desktop", "Themes", "Cairo.xaml");
    }

    /// <summary>A temp folder deleted on dispose.</summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cairoplus-test-" + Guid.NewGuid().ToString("N"));

        public TempDir()
        {
            Directory.CreateDirectory(Path);
        }

        public string Write(string relative, string content)
        {
            string full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}
