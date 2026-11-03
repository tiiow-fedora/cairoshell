#if CAIRO_FEATURES
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using CairoDesktop.Features.MonitorProfiles;
using Microsoft.Win32;

namespace CairoDesktop.Customization.Features.MonitorProfiles
{
    public sealed class MonitorProfilesChangedEventArgs : EventArgs
    {
        public MonitorProfilesChangedEventArgs(TopologyDiff diff, IReadOnlyList<MonitorInfo> monitors, IReadOnlyDictionary<string, ResolvedProfile> profiles, IReadOnlyList<string> changedProfileIds)
        {
            Diff = diff;
            Monitors = monitors;
            Profiles = profiles;
            ChangedProfileIds = changedProfileIds;
        }

        /// <summary>What changed in the monitor layout (normally empty for a <see cref="MonitorProfileWatcher.Refresh"/>).</summary>
        public TopologyDiff Diff { get; }

        public IReadOnlyList<MonitorInfo> Monitors { get; }

        /// <summary>Resolved settings for every monitor now present, keyed by device name.</summary>
        public IReadOnlyDictionary<string, ResolvedProfile> Profiles { get; }

        /// <summary>
        /// Device names whose effective settings differ from before, or that are new. A bar whose monitor only moved or
        /// changed size has the same settings and is not listed; also rebuild <see cref="Diff"/>.AffectedIds.
        /// </summary>
        public IReadOnlyList<string> ChangedProfileIds { get; }
    }

    /// <summary>
    /// Watches for monitor changes and raises <see cref="Changed"/> once per burst with a diff and the new resolved
    /// profiles. Create it, call <see cref="Start"/> on the UI thread, and Dispose it on shutdown: it hooks the static
    /// SystemEvents.DisplaySettingsChanged event, which would otherwise keep it alive forever.
    /// </summary>
    public sealed class MonitorProfileWatcher : IDisposable
    {
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(2);
        private const int MaxEmptyRetries = 5;
        private static readonly TimeSpan MinTick = TimeSpan.FromMilliseconds(20);

        private readonly Dispatcher _dispatcher;
        private readonly Func<ProfilesConfig> _configProvider;
        private readonly ChangeDebouncer _debouncer = new ChangeDebouncer(Quiet, MaxWait);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private volatile bool _disposed;
        private bool _started;
        private int _emptyRetries;
        private DispatcherTimer _timer;
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private Dictionary<string, ResolvedProfile> _profiles = new Dictionary<string, ResolvedProfile>();

        /// <param name="dispatcher">The UI dispatcher; events are raised on it.</param>
        /// <param name="configProvider">Returns the current config each time profiles are resolved (so a config reload is picked up).</param>
        public MonitorProfileWatcher(Dispatcher dispatcher, Func<ProfilesConfig> configProvider)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        }

        /// <summary>Raised on the UI thread after a display change has settled.</summary>
        public event EventHandler<MonitorProfilesChangedEventArgs> Changed;

        /// <summary>The monitors as of the last snapshot. Read on the UI thread.</summary>
        public IReadOnlyList<MonitorInfo> Monitors => _monitors;

        /// <summary>The resolved profiles as of the last snapshot. Read on the UI thread.</summary>
        public IReadOnlyDictionary<string, ResolvedProfile> Profiles => _profiles;

        /// <summary>Takes the first snapshot and starts listening. Must be called on the UI thread. Does not raise <see cref="Changed"/>.</summary>
        public void Start()
        {
            if (_disposed || _started) return;
            _dispatcher.VerifyAccess(); // before _started, so a wrong-thread call can be retried on the right one
            _started = true;

            _monitors = ScreenSnapshot.Capture();
            _profiles = ResolveProfiles(_monitors);

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = Quiet };
            _timer.Tick += OnTimerTick;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        /// <summary>Re-resolves the profiles now (after a config reload) and raises <see cref="Changed"/> with the profiles that changed. The diff is empty unless the monitors also changed since the last snapshot.</summary>
        public void Refresh()
        {
            if (_disposed || !_started) return;
            if (_dispatcher.CheckAccess()) SafeRecompute(true);
            else _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { if (!_disposed) SafeRecompute(true); }));
        }

        // Runs on whatever thread Windows uses for system events, never the UI thread.
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // An exception escaping a SystemEvents handler is unhandled on its worker thread and ends the process.
            try
            {
                if (_disposed || _dispatcher.HasShutdownStarted) return;
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(OnDisplayChangeOnUiThread));
            }
            catch (Exception ex)
            {
                Trace.TraceError("MonitorProfileWatcher could not queue a display change: " + ex);
            }
        }

        private void OnDisplayChangeOnUiThread()
        {
            if (_disposed || _timer == null) return;
            _debouncer.Notify(_clock.Elapsed);
            ArmTimer();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_disposed || _timer == null) return;
            if (_debouncer.ShouldFire(_clock.Elapsed))
            {
                _timer.Stop();
                if (SafeRecompute(false))
                {
                    _emptyRetries = 0;
                }
                else if (_emptyRetries < MaxEmptyRetries && !_disposed)
                {
                    // Windows reported no screens mid mode-switch and may not raise another event: look again soon.
                    _emptyRetries++;
                    _debouncer.Notify(_clock.Elapsed);
                    ArmTimer();
                }
                else
                {
                    _emptyRetries = 0;
                }
            }
            else
            {
                ArmTimer();
            }
        }

        private void ArmTimer()
        {
            if (_timer == null) return; // disposed from inside a Changed handler
            TimeSpan? wait = _debouncer.TimeUntilFire(_clock.Elapsed);
            _timer.Stop();
            if (wait == null) return;
            _timer.Interval = wait.Value < MinTick ? MinTick : wait.Value;
            _timer.Start();
        }

        // A throw out of a dispatcher callback (timer tick, BeginInvoke) takes the whole shell down, so nothing escapes here.
        private bool SafeRecompute(bool force)
        {
            try
            {
                return Recompute(force);
            }
            catch (Exception ex)
            {
                Trace.TraceError("MonitorProfileWatcher could not refresh the monitors: " + ex);
                return true;
            }
        }

        /// <returns>false when Windows reported no monitors (the caller may retry); true otherwise.</returns>
        private bool Recompute(bool force)
        {
            List<MonitorInfo> current = ScreenSnapshot.Capture();
            if (current.Count == 0) return false;

            TopologyDiff diff = MonitorTopologyDiff.Compare(_monitors, current);
            if (diff.IsEmpty && !force) return true;

            Dictionary<string, ResolvedProfile> profiles = ResolveProfiles(current);
            List<string> changed = ProfileResolver.ChangedMonitors(_profiles, profiles);
            _monitors = current;
            _profiles = profiles;

            EventHandler<MonitorProfilesChangedEventArgs> handler = Changed;
            if (handler == null) return true;
            try
            {
                handler(this, new MonitorProfilesChangedEventArgs(diff, current, profiles, changed));
            }
            catch (Exception ex)
            {
                Trace.TraceError("MonitorProfileWatcher subscriber failed: " + ex);
            }

            return true;
        }

        private Dictionary<string, ResolvedProfile> ResolveProfiles(List<MonitorInfo> monitors)
        {
            ProfilesConfig config;
            try
            {
                config = _configProvider();
            }
            catch (Exception ex)
            {
                Trace.TraceError("MonitorProfileWatcher could not read the config: " + ex);
                config = null;
            }

            return ProfileResolver.Resolve(monitors, config);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Must come off the static event on every path, or the watcher is never collected.
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Changed = null;

            if (_dispatcher.CheckAccess()) StopTimer();
            else if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(StopTimer));
        }

        private void StopTimer()
        {
            DispatcherTimer timer = _timer;
            _timer = null;
            if (timer == null) return;
            timer.Stop();
            timer.Tick -= OnTimerTick;
        }
    }
}

#endif
