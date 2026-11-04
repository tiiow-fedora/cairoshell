// Inert until CairoDesktop.Customization defines CAIRO_FEATURES (see docs/features/README.md). Never compiled on Linux.
#if CAIRO_FEATURES
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CairoDesktop.Customization.Themes;
using CairoDesktop.Features.WallpaperSlideshow;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.WallpaperSlideshow
{
    /// <summary>
    /// Changes the wallpaper on a timer. Create and call on the UI thread. The folder is listed on a worker thread (it may be a
    /// network share) and the result is applied back on the UI thread; a change that arrives after Dispose is dropped.
    /// The remaining time survives a restart because the last change time is saved in wallpaper-slideshow.json.
    /// </summary>
    internal sealed class SlideshowController : IDisposable
    {
        /// <summary>The running controller, so the "wallpaper" widget can ask for the next picture. Null when the slideshow is off.</summary>
        public static SlideshowController Current { get; private set; }

        private readonly SlideshowConfig _config;
        private readonly Dispatcher _dispatcher;
        private readonly Action<string> _log;
        private readonly string _stateFile = Path.Combine(CairoPlusPaths.DataFolder, "wallpaper-slideshow.json");
        private readonly SlideshowPlanner _planner;
        private readonly DispatcherTimer _timer;
        private readonly string _folder;
        private int _busy;
        private bool _disposed;

        public SlideshowController(SlideshowConfig config, Dispatcher dispatcher, Action<string> log)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _log = log ?? (_ => { });
            _folder = Environment.ExpandEnvironmentVariables(config.Folder ?? "");
            _planner = new SlideshowPlanner(null, LoadState());
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _timer.Tick += (s, e) => { _timer.Stop(); ChangeNow(); };
        }

        public void Start()
        {
            Current = this;
            TimeSpan interval = TimeSpan.FromMinutes(_config.IntervalMinutes);
            TimeSpan wait = _config.ChangeOnStart ? TimeSpan.Zero : _planner.UntilNext(DateTimeOffset.Now, interval);
            Schedule(wait);
        }

        /// <summary>Shows the next picture now and restarts the interval.</summary>
        public void ChangeNow()
        {
            if (_disposed || System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            _timer.Stop();
            bool shuffle = _config.Shuffle;
            SearchOption option = _config.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            Task.Run(() =>
            {
                try { return Directory.EnumerateFiles(_folder, "*", option).Where(ImageFilter.IsWallpaper).ToList(); }
                catch (Exception ex) { _log("wallpaper slideshow could not read '" + _folder + "': " + ex.Message); return new List<string>(); }
            }).ContinueWith(t => _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_disposed) return;
                    string pick = _planner.Next(t.Result, shuffle, DateTimeOffset.Now);
                    if (pick == null) _log("wallpaper slideshow: no pictures (jpg, png, bmp) in '" + _folder + "'.");
                    else
                    {
                        string error = WallpaperApplier.Apply(pick, _config.Style);
                        if (error != null) _log("wallpaper slideshow: " + error);
                        SaveState();
                    }
                }
                catch (Exception ex) { _log("wallpaper slideshow failed: " + ex.Message); }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _busy, 0);
                    if (!_disposed) Schedule(TimeSpan.FromMinutes(_config.IntervalMinutes));
                }
            })), TaskScheduler.Default);
        }

        private void Schedule(TimeSpan wait)
        {
            if (_disposed) return;
            _timer.Stop();
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            _timer.Interval = wait;
            _timer.Start();
        }

        private SlideshowState LoadState()
        {
            try { return File.Exists(_stateFile) ? SlideshowPlanner.Deserialize(File.ReadAllText(_stateFile)) : new SlideshowState(); }
            catch (Exception ex) { _log("wallpaper slideshow: could not read its state: " + ex.Message); return new SlideshowState(); }
        }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
                string temp = _stateFile + ".tmp";
                File.WriteAllText(temp, _planner.Serialize());
                if (File.Exists(_stateFile)) File.Delete(_stateFile);
                File.Move(temp, _stateFile);
            }
            catch (Exception ex) { _log("wallpaper slideshow: could not save its state: " + ex.Message); }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

    internal sealed class WallpaperButtonWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "wallpaper";

        public ICairoWidget Create(WidgetContext context) => new WallpaperButtonWidget(context);
    }

    /// <summary>A bar button that skips to the next wallpaper. Option: label (default "Next wallpaper"). Does nothing while the slideshow is off.</summary>
    internal sealed class WallpaperButtonWidget : TextWidget
    {
        private readonly string _label;

        public WallpaperButtonWidget(WidgetContext context) : base(context, TimeSpan.Zero)
        {
            _label = context.Options.GetString("label", "Next wallpaper");
            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnClick;
        }

        protected override string GetText() => _label;

        protected override string GetToolTip() => SlideshowController.Current == null
            ? "The wallpaper slideshow is off (set wallpaperSlideshow.enabled in cairo-plus.json)."
            : "Change the wallpaper now.";

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            SlideshowController.Current?.ChangeNow();
            e.Handled = true;
        }

        public override void Dispose()
        {
            TextBlock.MouseLeftButtonUp -= OnClick;
            base.Dispose();
        }
    }
}
#endif
