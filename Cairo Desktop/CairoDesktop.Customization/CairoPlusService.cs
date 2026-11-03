using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CairoDesktop.Application.Interfaces;
using CairoDesktop.Customization.Actions;
using CairoDesktop.Customization.Bars;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Hotkeys;
using CairoDesktop.Customization.Themes;
using CairoDesktop.Customization.Widgets;
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
        private readonly ICommandService _commandService;
        private readonly ILogger<CairoPlusService> _logger;
        private readonly Dictionary<Window, BarCustomizer> _bars = new Dictionary<Window, BarCustomizer>();
        private WidgetRegistry _widgets;
        private readonly HotkeyRegistry _hotkeys = new HotkeyRegistry();
        private ActionRunner _actions;

        private FileSystemWatcher _configWatcher;
        private FileSystemWatcher _packWatcher;
        private DispatcherTimer _reloadTimer;
        private Dispatcher _dispatcher;
        private bool _started;
        private DateTime _ignoreConfigEventsUntil;
        private DateTime _lastReloadHookUtc = DateTime.MinValue;
        private bool _windowsUsesLightMode;

        private CairoPlusConfig _config = new CairoPlusConfig();
        private readonly List<string> _problems = new List<string>();

        /// <summary>The running instance, or null before Cairo starts it.</summary>
        public static CairoPlusService Current { get; private set; }

        public ResolvedSettings Settings { get; private set; } = ResolvedSettings.Disabled;

        /// <summary>The config as last loaded successfully. Edits with syntax errors keep the previous one live.</summary>
        public CairoPlusConfig Config => _config;

        /// <summary>Errors and warnings from the last load, for the settings UI and log.</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>Hotkeys currently registered, e.g. "Ctrl+Alt+T → theme next".</summary>
        public IReadOnlyList<string> ActiveHotkeys => _hotkeys.Active;

        /// <summary>Raised on the UI thread after settings were (re)applied.</summary>
        public event EventHandler Applied;

        public CairoPlusService(IThemeService themeService, ICommandService commandService, ILogger<CairoPlusService> logger)
        {
            _themeService = themeService;
            _commandService = commandService;
            _logger = logger;
        }

        #region IShellExtension
        public void Start()
        {
            Current = this;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _started = true;

            _actions = new ActionRunner(_commandService, this);
            _widgets = new WidgetRegistry();
            _widgets.LoadExternal(CairoPlusPaths.WidgetFolders);

            CairoPlusHooks.BarRegistered += OnBarRegistered;
            CairoPlusHooks.BarUnregistered += OnBarUnregistered;
            CairoPlusHooks.BarItemsChanging += OnBarItemsChanging;
            CairoPlusHooks.ReservedWidthProvider = window => _bars.TryGetValue(window, out var bar) ? bar.ReservedWidth : null;
            CairoPlusHooks.ExtraThicknessProvider = window => _bars.TryGetValue(window, out var bar) ? bar.ExtraThickness : 0;
            CairoPlusHooks.HorizontalInsetProvider = window => _bars.TryGetValue(window, out var bar) ? bar.HorizontalInset : 0;

            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            Load();

            // Cairo loads its theme right after extensions start, which picks up our hooks;
            // everything that isn't theme-driven is applied here.
            ApplyNonThemeSettings();
            foreach (var host in CairoPlusHooks.Bars) OnBarRegistered(host);
            StartWatching();

            RunHooks("onStart");
        }

        public void Stop()
        {
            _started = false;
            StopWatching();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _hotkeys.Clear();

            CairoPlusHooks.BarRegistered -= OnBarRegistered;
            CairoPlusHooks.BarUnregistered -= OnBarUnregistered;
            CairoPlusHooks.BarItemsChanging -= OnBarItemsChanging;
            CairoPlusHooks.ReservedWidthProvider = null;
            CairoPlusHooks.ExtraThicknessProvider = null;
            CairoPlusHooks.HorizontalInsetProvider = null;
            foreach (var bar in _bars.Values) bar.Restore();
            _bars.Clear();
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

            string previousTheme = Settings.Pack?.Id;
            bool previousLight = Settings.UsesLightAppearance;

            Load();
            ApplyTheme();
            ApplyNonThemeSettings();
            ApplyBars();
            RestartPackWatcher();
            Applied?.Invoke(this, EventArgs.Empty);

            bool appearanceChanged = Settings.Pack != null && previousLight != Settings.UsesLightAppearance;
            if (appearanceChanged || !string.Equals(previousTheme, Settings.Pack?.Id, StringComparison.OrdinalIgnoreCase))
            {
                RunHooks("onThemeApplied");
            }

            RunHooks("onConfigReloaded");
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
            foreach (string problem in _widgets?.LoadProblems ?? new List<string>()) AddProblem(problem);

            var result = ConfigLoader.LoadFile(CairoPlusPaths.ConfigFile);
            if (result.Success)
            {
                _config = result.Config;
                foreach (string problem in ConfigChecker.Check(_config)) AddProblem($"{CairoPlusPaths.ConfigFileName} {problem}");
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

            _windowsUsesLightMode = WindowsAppearance.UsesLightMode();
            Settings = ResolvedSettings.Resolve(_config, pack, _windowsUsesLightMode);

            if (Settings.PackHooksBlocked)
            {
                AddProblem($"Theme pack '{Settings.Pack?.Id}' defines hooks (programs to run when it is applied), which are off. " +
                           "Read them in its theme.json and set \"allowPackHooks\": true in cairo-plus.json to run them.");
            }

            var resolved = Settings;
            if (resolved.Pack != null)
            {
                string baseTheme = string.IsNullOrWhiteSpace(resolved.Pack.Manifest.BaseTheme) ? "Default" : resolved.Pack.Manifest.BaseTheme;
                CairoPlusHooks.SetTheme(baseTheme, resources => BuildThemeLayer(resolved, resources));
            }
            else if (resolved.Enabled && ThemeLayerBuilder.NeedsOpacityLayer(resolved))
            {
                // No pack, but bar transparency still works on top of Cairo's own theme.
                CairoPlusHooks.SetTheme(null, resources => BuildThemeLayer(resolved, resources));
            }
            else
            {
                CairoPlusHooks.SetTheme(null, null);
            }

            _logger.LogInformation($"CairoPlus: loaded (enabled: {Settings.Enabled}, theme pack: {Settings.Pack?.Id ?? "none"})");
        }

        private ResourceDictionary BuildThemeLayer(ResolvedSettings settings, ResourceDictionary resources)
        {
            var warnings = new List<string>();
            Func<string, object> findExisting = key => resources.Contains(key) ? resources[key] : FindInMerged(resources, key);
            var pack = settings.Pack;

            var layer = pack != null ? ThemeLayerBuilder.Build(pack, findExisting, warnings) : new ResourceDictionary();
            ThemeLayerBuilder.ApplyBarOpacity(layer, settings, findExisting);

            foreach (string warning in warnings)
            {
                AddProblem($"Theme pack '{pack?.Id}': {warning}");
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
            _hotkeys.Apply(Settings.Enabled ? Settings.Hotkeys : null, _actions, AddProblem);

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

        #region Hooks
        /// <summary>
        /// Runs the commands configured for a hook (see <see cref="HooksConfig"/>), handing them the current theme
        /// as CAIRO_* environment variables. A failing hook is reported, never thrown.
        /// </summary>
        private void RunHooks(string hookName)
        {
            if (!Settings.Enabled || _actions == null) return;

            var hooks = Settings.Hooks.Get(hookName);
            if (hooks.Count == 0) return;

            if (hookName == "onConfigReloaded")
            {
                // A hook that edits cairo-plus.json would otherwise re-trigger itself forever.
                if (DateTime.UtcNow - _lastReloadHookUtc < TimeSpan.FromSeconds(2))
                {
                    AddProblem("onConfigReloaded hooks ran again within 2 seconds and were skipped (does a hook edit cairo-plus.json?).");
                    return;
                }

                _lastReloadHookUtc = DateTime.UtcNow;
            }

            var environment = HookEnvironment.Build(hookName, Settings);
            foreach (var hook in hooks)
            {
                try
                {
                    string error = HookAction.Validate(hook) ?? _actions.Run(hook.Action, hook.Arg, hook.Args, environment);
                    if (error != null) AddProblem($"hook {hookName}: {error}");
                }
                catch (Exception ex)
                {
                    AddProblem($"hook {hookName}: {ex.Message}");
                }
            }
        }
        #endregion

        #region Bars
        private void OnBarRegistered(BarHost host)
        {
            if (!_started || _bars.ContainsKey(host.Window)) return;
            _logger.LogDebug($"CairoPlus: bar registered: {Describe(host)}");
            var customizer = new BarCustomizer(host);
            _bars[host.Window] = customizer;
            ApplyBar(customizer);
        }

        private void OnBarUnregistered(BarHost host)
        {
            _logger.LogDebug($"CairoPlus: bar unregistered: {Describe(host)}");
            if (_bars.TryGetValue(host.Window, out var customizer))
            {
                customizer.Restore();
                _bars.Remove(host.Window);
            }
        }

        private void OnBarItemsChanging(Window window, bool starting)
        {
            if (!_bars.TryGetValue(window, out var customizer)) return;
            _logger.LogDebug($"CairoPlus: bar items {(starting ? "changing" : "changed")}: {Describe(customizer.Host)}");
            if (starting) customizer.Restore();
            else ApplyBar(customizer);
        }

        private static string Describe(BarHost host)
        {
            var bounds = new Rect(host.Window.Left, host.Window.Top, host.Window.Width, host.Window.Height);
            return $"{host.Kind} #{host.Window.GetHashCode():X} at {bounds}";
        }

        /// <summary>Which monitor a bar window is on, for the "monitors" overrides. Falls back to the primary monitor.</summary>
        private static MonitorInfo DescribeMonitor(Window window)
        {
            var screen = (window as ManagedShell.AppBar.AppBarWindow)?.Screen;
            if (screen == null) return new MonitorInfo { Index = 1, DeviceName = "", IsPrimary = true };

            var screens = System.Windows.Forms.Screen.AllScreens;
            string primary = screens.FirstOrDefault(s => s.Primary)?.DeviceName;
            return MonitorInfo.Create(screen.DeviceName, screen.Primary, primary, screens.Select(s => s.DeviceName));
        }

        private void ApplyBars()
        {
            foreach (var customizer in _bars.Values.ToList()) ApplyBar(customizer);
        }

        private void ApplyBar(BarCustomizer customizer)
        {
            bool taskbar = customizer.Host.Kind == CairoDesktop.Widgets.Sdk.WidgetBar.Taskbar;
            try
            {
                // Settings.GetBar is null when Cairo Plus is off; it adds any matching "monitors" entries.
                var config = Settings.GetBar(taskbar, DescribeMonitor(customizer.Host.Window));
                customizer.Apply(config, Settings, _widgets, _actions, AddProblem);
            }
            catch (Exception ex)
            {
                AddProblem($"Could not apply the bar layout: {ex.Message}");
                try { customizer.Restore(); } catch { /* leave the bar as it is */ }
            }
        }
        #endregion

        private void AddProblem(string message)
        {
            if (_problems.Contains(message)) return;
            _problems.Add(message);
            _logger.LogWarning($"CairoPlus: {message}");
        }
        #endregion

        #region Live reload
        /// <summary>
        /// Windows raises this (on another thread) when its settings change, which includes the light/dark app mode.
        /// If the mode flipped, re-apply through the same debounce as a file change.
        /// </summary>
        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            _dispatcher?.BeginInvoke(new Action(() =>
            {
                if (!_started || _reloadTimer == null) return;
                if (WindowsAppearance.UsesLightMode() == _windowsUsesLightMode) return;

                _reloadTimer.Stop();
                _reloadTimer.Start();
            }));
        }

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
