// Inert until CairoDesktop.Customization defines CAIRO_FEATURES (see docs/features/README.md). Never compiled on Linux.
#if CAIRO_FEATURES
using System;
using System.Windows.Threading;
using CairoDesktop.Features.ThemeSchedule;

namespace CairoDesktop.Customization.Features.ThemeSchedule
{
    /// <summary>
    /// Raises <see cref="Changed"/> (UI thread) whenever the schedule flips between light and dark, and once from <see cref="Start"/>
    /// with the current answer. One timer: it sleeps until the next change, but never longer than 6 hours, so a clock change,
    /// time-zone change or a wake from sleep corrects itself without extra hooks. Call everything on the UI thread.
    /// </summary>
    internal sealed class AppearanceScheduler : IDisposable
    {
        private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(6);
        private static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(5);

        private readonly AppearanceSchedule _schedule;
        private readonly DispatcherTimer _timer;
        private bool? _last;
        private bool _disposed;

        public AppearanceScheduler(AppearanceSchedule schedule, Dispatcher dispatcher)
        {
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)));
            _timer.Tick += (s, e) => Evaluate();
        }

        /// <summary>True means the pack's light version.</summary>
        public event Action<bool> Changed;

        public bool? Current => _last;

        public void Start() => Evaluate();

        /// <summary>Re-evaluate now, for example after Windows reports a time or settings change.</summary>
        public void Evaluate()
        {
            if (_disposed) return;
            _timer.Stop();
            DateTimeOffset now = DateTimeOffset.Now;
            ScheduleDecision decision = _schedule.Decide(now);
            TimeSpan sleep = decision.NextChange.HasValue ? decision.NextChange.Value - now + TimeSpan.FromSeconds(1) : MaxSleep;
            if (sleep > MaxSleep) sleep = MaxSleep;
            if (sleep < MinSleep) sleep = MinSleep;
            _timer.Interval = sleep;
            _timer.Start();

            if (_last == decision.IsLight) return;
            _last = decision.IsLight;
            try { Changed?.Invoke(decision.IsLight); }
            catch (Exception) { /* a subscriber must not stop the schedule; the next tick tries again only on a change */ }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();
        }
    }
}
#endif
