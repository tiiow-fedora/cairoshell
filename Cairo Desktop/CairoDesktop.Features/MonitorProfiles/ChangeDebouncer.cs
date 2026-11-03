using System;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>
    /// Pure state machine that collapses a burst of change notifications into one. Time is supplied by the caller
    /// (any monotonic <see cref="TimeSpan"/>, for example a Stopwatch's Elapsed), so no clock or timer lives here.
    /// Fires once the notifications have been quiet for <see cref="Quiet"/>, or <see cref="MaxWait"/> after the first
    /// one of a burst even if they keep coming. A clock that jumps backwards re-anchors the burst instead of stalling it.
    /// </summary>
    public sealed class ChangeDebouncer
    {
        private bool _pending;
        private TimeSpan _first;
        private TimeSpan _last;

        public ChangeDebouncer(TimeSpan quiet, TimeSpan maxWait)
        {
            if (quiet < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quiet));
            if (maxWait < quiet) maxWait = quiet;
            Quiet = quiet;
            MaxWait = maxWait;
        }

        public TimeSpan Quiet { get; }
        public TimeSpan MaxWait { get; }
        public bool IsPending => _pending;

        public void Notify(TimeSpan now)
        {
            if (!_pending || now < _last)
            {
                _first = now;
                _pending = true;
            }
            _last = now;
        }

        /// <summary>True exactly once per burst, when it is time to act. Resets the debouncer.</summary>
        public bool ShouldFire(TimeSpan now)
        {
            if (!_pending) return false;
            if (now < _last)
            {
                _first = now;
                _last = now;
                return false;
            }
            if (now - _last >= Quiet || now - _first >= MaxWait)
            {
                _pending = false;
                return true;
            }
            return false;
        }

        /// <summary>How long until <see cref="ShouldFire"/> would return true; null when nothing is pending.</summary>
        public TimeSpan? TimeUntilFire(TimeSpan now)
        {
            if (!_pending) return null;
            if (now < _last) return Quiet;
            TimeSpan byQuiet = _last + Quiet - now;
            TimeSpan byMax = _first + MaxWait - now;
            TimeSpan t = byQuiet < byMax ? byQuiet : byMax;
            return t < TimeSpan.Zero ? TimeSpan.Zero : t;
        }

        public void Reset() => _pending = false;
    }
}
