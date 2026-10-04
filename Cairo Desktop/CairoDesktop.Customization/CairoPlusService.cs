using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Themes;
using Microsoft.Extensions.Logging;

namespace CairoDesktop.Customization
{
    /// <summary>
    /// Orchestrates Cairo Plus: loads cairo-plus.json and the active theme pack, applies them,
    /// and re-applies everything when either changes on disk.
    /// Registered as an inbox shell extension so it starts before Cairo loads its theme and windows.
    /// </summary>
    public sealed class CairoPlusService : IShellExtension, IDisposable
    {
        private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(400);

        private readonly IThemeService _themeService;
        private readonly ILogger<CairoPlusService> _logger;

        private FileSystemWatcher _configWatcher;
        private FileSystemWatcher _packWatcher;
        private DispatcherTimer _reloadTimer;
        private Dispatcher _dispatcher;
        private bool _started;
        private DateTime _ignoreConfigEventsUntil;

        private CairoPlusConfig _config = new CairoPlusConfig();
        private readonly List<string> _problems = new List<string>();

        /// <summary>The running instance, or null before Cairo starts it.</summary>
        public static CairoPlusService Current { get; private set; }

        public ResolvedSettings Settings { get; private set; } = ResolvedSettings.Disabled;

        /// <summary>The config as last loaded successfully. Edits with syntax errors keep the previous one live.</summary>
        public CairoPlusConfig Config => _config;

        /// <summary>Errors and warnings from the last load, for the settings UI and log.</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>Raised on the UI thread after settings were (re)applied.</summary>
        public event EventHandler Applied;

        public CairoPlusService(IThemeService themeService, ILogger<CairoPlusService> logger)
        {
            _themeService = themeService;
            _logger = logger;
        }

        #region IShellExtension
        public void Start()
        {
            Current = this;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _started = true;

            Load();

            // Cairo loads its theme right after extensions start, which picks up our hooks;
            // everything that isn't theme-driven is applied here.
            ApplyNonThemeSettings();
            StartWatching();
        }

        public void Stop()
        {
            _started = false;
            StopWatching();
            if (Current == this) Current = null;
        }

        public void Dispose() => Stop();
        #endregion

        #region Public API (settings UI, command line, hotkeys)
        public IReadOnlyList<ThemePack> GetThemePacks()
        {
            return ThemePackLoader.Discover(CairoPlusPaths.ThemePackFolders);
        }

        /// <summary>Activates a theme pack (null = none) and enables Cairo Plus, persisting the choice in cairo-plus.json.</summary>
        public void ApplyTheme(string id)
        {
            IgnoreOwnConfigWrite();
            WriteConfigProperties(id, setTheme: true, enabled: true);
            Reload();
        }

        /// <summary>Switches to the next theme pack alphabetically (wraps around).</summary>
        public void ApplyNextTheme()
        {
            var packs = GetThemePacks().Where(p => p.Error == null).ToList();
            if (packs.Count == 0) return;

            int index = packs.FindIndex(p => string.Equals(p.Id, Settings.Pack?.Id, StringComparison.OrdinalIgnoreCase));
            ApplyTheme(packs[(index + 1) % packs.Count].Id);
        }

        public void SetEnabled(bool enabled)
        {
            IgnoreOwnConfigWrite();
            WriteConfigProperties(null, setTheme: false, enabled: enabled);
            Reload();
        }

        /// <summary>Re-reads config and theme pack and re-applies everything. Must run on the UI thread.</summary>
        public void Reload()
        {
            if (!_started) return;

            Load();
            ApplyTheme();
            ApplyNonThemeSettings();
            RestartPackWatcher();
            Applied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Updates "theme"/"enabled" in cairo-plus.json without disturbing comments, creating the file from the
        /// template if it doesn't exist. Static so the command-line switch can use it without a running shell.
        /// </summary>
        public static void WriteConfigProperties(string themeId, bool setTheme, bool? enabled)
        {
            string path = CairoPlusPaths.ConfigFile;
            string json = File.Exists(path) ? ConfigLoader.ReadAllTextShared(path) : ConfigTemplate.Text;

            if (setTheme) json = JsonTextEditor.SetString(json, "theme", themeId);
            if (enabled.HasValue) json = JsonTextEditor.SetBool(json, "enabled", enabled.Value);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }
        #endregion

        #region Loading and applying
        private void Load()
        {
            _problems.Clear();

            var result = ConfigLoader.LoadFile(CairoPlusPaths.ConfigFile);
            if (result.Success)
            {
                _config = result.Config;
            }
            else
            {
                // Keep the last good config live so a half-typed edit doesn't wreck the desktop.
                AddProblem($"{CairoPlusPaths.ConfigFileName} {result.Error} (keeping the previous settings)");
            }

            ThemePack pack = null;
            if (_config.Enabled && !string.IsNullOrWhiteSpace(_config.Theme))
            {
                pack = ThemePackLoader.Find(CairoPlusPaths.ThemePackFolders, _config.Theme);
                if (pack == null)
                {
                    AddProblem($"Theme pack '{_config.Theme}' was not found in {string.Join(" or ", CairoPlusPaths.ThemePackFolders)}.");
                }
                else if (pack.Error != null)
                {
                    AddProblem($"Theme pack '{pack.Id}': {pack.Error}");
                }
            }

            Settings = ResolvedSettings.Resolve(_config, pack);

            if (Settings.Pack != null)
            {
                string baseTheme = string.IsNullOrWhiteSpace(Settings.Pack.Manifest.BaseTheme) ? "Default" : Settings.Pack.Manifest.BaseTheme;
                var activePack = Settings.Pack;
                CairoPlusHooks.SetTheme(baseTheme, resources => BuildThemeLayer(activePack, resources));
            }
            else
            {
                CairoPlusHooks.SetTheme(null, null);
            }

            _logger.LogInformation($"CairoPlus: loaded (enabled: {Settings.Enabled}, theme pack: {Settings.Pack?.Id ?? "none"})");
        }

        private ResourceDictionary BuildThemeLayer(ThemePack pack, ResourceDictionary resources)
        {
            var warnings = new List<string>();
            var layer = ThemeLayerBuilder.Build(pack, key => resources.Contains(key) ? resources[key] : FindInMerged(resources, key), warnings);
            foreach (string warning in warnings)
            {
                AddProblem($"Theme pack '{pack.Id}': {warning}");
            }

            return layer;
        }

        private static object FindInMerged(ResourceDictionary resources, string key)
        {
            for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dictionary = resources.MergedDictionaries[i];
                if (dictionary.Contains(key)) return dictionary[key];
            }

            return null;
        }

        private void ApplyTheme()
        {
            // Cairo's theme service reloads its XAML theme and then calls CairoPlusHooks.OnThemeApplied.
            _themeService.SetThemeFromSettings();
        }

        private void ApplyNonThemeSettings()
        {
            AppIconOverrides.Instance.SetOverrides(Settings.AppIcons);

            if (Settings.Pack != null)
            {
                string wallpaper = Settings.GetWallpaperPath();
                if (wallpaper != null)
                {
                    string error = WallpaperApplier.Apply(wallpaper, Settings.Pack.Manifest.Wallpaper?.Style);
                    if (error != null) AddProblem($"Theme pack '{Settings.Pack.Id}': {error}");
                }
                else
                {
                    WallpaperApplier.Apply(null, null);
                }
            }
            else
            {
                WallpaperApplier.Apply(null, null);
            }
        }

        private void AddProblem(string message)
        {
            if (_problems.Contains(message)) return;
            _problems.Add(message);
            _logger.LogWarning($"CairoPlus: {message}");
        }
        #endregion

        #region Live reload
        private void StartWatching()
        {
            _reloadTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = ReloadDebounce };
            _reloadTimer.Tick += (s, e) =>
            {
                _reloadTimer.Stop();
                Reload();
            };

            try
            {
                Directory.CreateDirectory(CairoPlusPaths.DataFolder);
                _configWatcher = new FileSystemWatcher(CairoPlusPaths.DataFolder, CairoPlusPaths.ConfigFileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                HookWatcher(_configWatcher);
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AddProblem($"Live reload is unavailable: {ex.Message}");
            }

            RestartPackWatcher();
        }

        private void RestartPackWatcher()
        {
            string folder = Settings.Pack?.Folder;
            if (_packWatcher != null && string.Equals(_packWatcher.Path, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _packWatcher?.Dispose();
            _packWatcher = null;

            if (folder == null || !Directory.Exists(folder)) return;

            try
            {
                _packWatcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.DirectoryName
                };
                HookWatcher(_packWatcher);
                _packWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AddProblem($"Live reload of the theme pack is unavailable: {ex.Message}");
            }
        }

        private void HookWatcher(FileSystemWatcher watcher)
        {
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
        }

        private void IgnoreOwnConfigWrite()
        {
            // We reload directly after writing the file ourselves; don't reload a second time from the watcher.
            _ignoreConfigEventsUntil = DateTime.UtcNow.AddSeconds(1.5);
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (sender == _configWatcher && DateTime.UtcNow < _ignoreConfigEventsUntil) return;

            // Editors fire several events per save; restart the debounce timer on each.
            _dispatcher?.BeginInvoke(new Action(() =>
            {
                if (_reloadTimer == null) return;
                _reloadTimer.Stop();
                _reloadTimer.Start();
            }));
        }

        private void StopWatching()
        {
            _reloadTimer?.Stop();
            _reloadTimer = null;
            _configWatcher?.Dispose();
            _configWatcher = null;
            _packWatcher?.Dispose();
            _packWatcher = null;
        }
        #endregion
    }
}
