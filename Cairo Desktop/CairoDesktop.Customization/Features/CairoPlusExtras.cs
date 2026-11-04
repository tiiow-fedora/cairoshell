// Inert until built with -p:CairoExtras=true (see docs/features/WINDOWS-VERIFY.md). Never compiled on Linux.
#if CAIRO_FEATURES || CAIRO_EXTRAS
using System;
using System.Windows.Threading;
using CairoDesktop.Customization.Features.ThemeSchedule;
using CairoDesktop.Customization.Features.WallpaperSlideshow;
using CairoDesktop.Features.ThemeSchedule;
using CairoDesktop.Features.WallpaperSlideshow;

namespace CairoDesktop.Customization.Features
{
    /// <summary>
    /// Glue between CairoPlusService and the two config-section features (theme schedule, wallpaper slideshow).
    /// The widget features (focus, keepawake, worldclock, wallpaper button) need nothing here: WidgetRegistry creates them.
    /// UI thread only.
    /// </summary>
    internal sealed class CairoPlusExtras : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<string> _problem;
        private readonly Action _requestReload;
        private SlideshowController _slideshow;
        private string _slideshowKey;
        private AppearanceScheduler _scheduler;
        private string _scheduleKey;

        public CairoPlusExtras(Dispatcher dispatcher, Action<string> problem, Action requestReload)
        {
            _dispatcher = dispatcher;
            _problem = problem;
            _requestReload = requestReload;
        }

        /// <summary>The scheduled answer for "use the light version?", or null when no schedule is enabled. No side effects.</summary>
        public static bool? ScheduledLight(string configText, TimeZoneInfo zone, DateTimeOffset now)
        {
            ScheduleConfig config = ScheduleConfigParser.ParseDocument(configText).Config;
            if (!config.Enabled) return null;
            try { return new AppearanceSchedule(config, zone).Decide(now).IsLight; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>Starts, restarts or stops the features to match the config text. Safe to call on every reload; unchanged sections are left running.</summary>
        public void Apply(string configText, bool cairoPlusEnabled)
        {
            ApplySlideshow(configText, cairoPlusEnabled);
            ApplySchedule(configText, cairoPlusEnabled);
        }

        private void ApplySlideshow(string configText, bool cairoPlusEnabled)
        {
            SlideshowConfigResult result = SlideshowConfigParser.ParseDocument(configText);
            foreach (SlideshowFinding finding in result.Findings) _problem(finding.ToString());

            SlideshowConfig c = result.Config;
            string key = cairoPlusEnabled && c.Enabled
                ? string.Join("|", c.Folder, c.IntervalMinutes, c.Shuffle, c.IncludeSubfolders, c.Style, c.ChangeOnStart)
                : null;
            if (key == _slideshowKey) return;

            _slideshow?.Dispose();
            _slideshow = null;
            _slideshowKey = key;
            if (key == null) return;

            _slideshow = new SlideshowController(c, _dispatcher, message => _problem(message));
            _slideshow.Start();
        }

        private void ApplySchedule(string configText, bool cairoPlusEnabled)
        {
            ScheduleConfigResult result = ScheduleConfigParser.ParseDocument(configText);
            foreach (ScheduleFinding finding in result.Findings) _problem(finding.ToString());

            ScheduleConfig c = result.Config;
            string key = cairoPlusEnabled && c.Enabled
                ? string.Join("|", c.Mode, c.LightFrom, c.DarkFrom, c.Latitude, c.Longitude, c.SunriseOffsetMinutes, c.SunsetOffsetMinutes)
                : null;
            if (key == _scheduleKey) return;

            _scheduler?.Dispose();
            _scheduler = null;
            _scheduleKey = key;
            if (key == null) return;

            try
            {
                _scheduler = new AppearanceScheduler(new AppearanceSchedule(c, TimeZoneInfo.Local), _dispatcher);
                _scheduler.Start();                                    // records the current answer; nobody is listening yet, so no reload loop
                _scheduler.Changed += light => _requestReload();       // Load() asks ScheduledLight again and picks the matching variant
            }
            catch (ArgumentException ex)
            {
                _problem("themeSchedule: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _slideshow?.Dispose();
            _scheduler?.Dispose();
            _slideshow = null;
            _scheduler = null;
            _slideshowKey = null;
            _scheduleKey = null;
        }
    }
}
#endif
