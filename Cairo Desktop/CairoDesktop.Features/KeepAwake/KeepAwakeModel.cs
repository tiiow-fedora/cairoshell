using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Features.KeepAwake
{
    public sealed class KeepAwakeOptions
    {
        /// <summary>Durations a click steps through, in minutes, after "off". 0 means "until turned off". Always ends with 0.</summary>
        public IList<int> PresetMinutes { get; set; } = new List<int> { 30, 60, 120, 0 };
        /// <summary>Also stop the screen from turning off (otherwise only sleep is prevented).</summary>
        public bool KeepDisplayOn { get; set; }
        /// <summary>Text before the state, e.g. "Awake" gives "Awake 0:42".</summary>
        public string Label { get; set; } = "Awake";

        /// <summary>Drops unusable presets (negative, over a week), de-duplicates, makes sure "until off" is last. Returns a message per problem.</summary>
        public IList<string> Normalize()
        {
            var findings = new List<string>();
            var cleaned = new List<int>();
            foreach (int m in PresetMinutes ?? new List<int>())
            {
                if (m < 0 || m > 10080) { findings.Add($"presetMinutes: {m} is outside 0..10080 and was dropped."); continue; }
                if (m != 0 && !cleaned.Contains(m)) cleaned.Add(m);
            }

            if (cleaned.Count == 0 && PresetMinutes != null && PresetMinutes.Count > 0 && PresetMinutes.All(m => m < 0 || m > 10080))
                findings.Add("presetMinutes: no usable value; using the defaults.");
            if (cleaned.Count == 0) cleaned.AddRange(new[] { 30, 60, 120 });
            cleaned.Add(0);
            PresetMinutes = cleaned;
            if (string.IsNullOrWhiteSpace(Label)) Label = "Awake";
            return findings;
        }
    }

    /// <summary>
    /// "Caffeine" state: off, on for N minutes, or on until turned off. Takes <c>now</c> everywhere; no clock, no Win32.
    /// The adapter asks <see cref="IsActive"/> and tells Windows to stay awake (or not) accordingly.
    /// </summary>
    public sealed class KeepAwakeModel
    {
        private readonly KeepAwakeOptions _options;
        private int _presetIndex = -1;          // -1 = off
        private DateTimeOffset _start;
        private TimeSpan? _duration;            // null = until turned off

        public KeepAwakeModel(KeepAwakeOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Normalize();
        }

        public bool KeepDisplayOn => _options.KeepDisplayOn;

        public bool IsOn => _presetIndex >= 0;

        /// <summary>True while Windows should be kept awake.</summary>
        public bool IsActive(DateTimeOffset now) => IsOn && (_duration == null || Remaining(now) > TimeSpan.Zero);

        /// <summary>Time left for a timed state; null when off or on until turned off.</summary>
        public TimeSpan? RemainingOrNull(DateTimeOffset now) => IsOn && _duration != null ? Remaining(now) : (TimeSpan?)null;

        private TimeSpan Remaining(DateTimeOffset now)
        {
            TimeSpan left = _duration.Value - (now - _start);
            if (left < TimeSpan.Zero) return TimeSpan.Zero;
            return left > _duration.Value ? _duration.Value : left; // a clock moved backwards cannot extend the time
        }

        /// <summary>Off, then each preset in turn, then back to off (what a click does).</summary>
        public void Cycle(DateTimeOffset now)
        {
            int next = _presetIndex + 1;
            if (next >= _options.PresetMinutes.Count) TurnOff();
            else Apply(next, now);
        }

        public void TurnOff()
        {
            _presetIndex = -1;
            _duration = null;
        }

        /// <summary>Turn on for a number of minutes (clamped to 1..10080), or until off when minutes is 0.</summary>
        public void TurnOn(int minutes, DateTimeOffset now)
        {
            int index = _options.PresetMinutes.IndexOf(minutes);
            _presetIndex = index >= 0 ? index : 0;
            _start = now;
            _duration = minutes <= 0 ? (TimeSpan?)null : TimeSpan.FromMinutes(Math.Min(minutes, 10080));
        }

        private void Apply(int index, DateTimeOffset now)
        {
            int minutes = _options.PresetMinutes[index];
            _presetIndex = index;
            _start = now;
            _duration = minutes == 0 ? (TimeSpan?)null : TimeSpan.FromMinutes(minutes);
        }

        /// <summary>Call about once a minute. Returns true exactly once, when a timed state just ran out (and the model went back to off).</summary>
        public bool Tick(DateTimeOffset now)
        {
            if (!IsOn || _duration == null || Remaining(now) > TimeSpan.Zero) return false;
            TurnOff();
            return true;
        }

        /// <summary>"Awake: off", "Awake: on" (until turned off) or "Awake 0:42" (hours:minutes left, rounded up).</summary>
        public string FormatText(DateTimeOffset now)
        {
            if (!IsOn) return _options.Label + ": off";
            if (_duration == null) return _options.Label + ": on";
            long minutes = (long)Math.Ceiling(Remaining(now).TotalMinutes);
            return $"{_options.Label} {minutes / 60}:{minutes % 60:00}";
        }

        public string FormatToolTip(DateTimeOffset now)
        {
            string what = _options.KeepDisplayOn ? "Keeps the PC and screen awake" : "Keeps the PC from sleeping";
            if (!IsOn) return what + " when on. Click to turn on for " + DescribeNext() + ".";
            return (_duration == null ? what + " until you turn it off." : what + " for " + FormatText(now).Substring(_options.Label.Length + 1) + " more.")
                   + " Click for " + DescribeNext() + ".";
        }

        private string DescribeNext()
        {
            int next = _presetIndex + 1;
            if (next >= _options.PresetMinutes.Count) return "off";
            int m = _options.PresetMinutes[next];
            if (m == 0) return "as long as needed";
            if (m < 60) return m + " minutes";
            return m % 60 == 0 ? (m / 60) + (m == 60 ? " hour" : " hours") : (m / 60) + "h " + (m % 60) + "m";
        }
    }
}
