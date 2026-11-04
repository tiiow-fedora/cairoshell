using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.FocusTimer
{
    public enum FocusPhase { Work, ShortBreak, LongBreak }

    public enum FocusState { Idle, Running, Paused }

    /// <summary>Settings for the focus (Pomodoro) timer. <see cref="Normalize"/> clamps everything into a usable range.</summary>
    public sealed class FocusTimerOptions
    {
        public int WorkMinutes { get; set; } = 25;
        public int ShortBreakMinutes { get; set; } = 5;
        public int LongBreakMinutes { get; set; } = 15;
        /// <summary>A long break replaces the short one after this many completed work sessions.</summary>
        public int SessionsBeforeLongBreak { get; set; } = 4;
        /// <summary>When true the next phase starts by itself; otherwise the timer waits (Idle) for a click.</summary>
        public bool AutoStartNext { get; set; }

        /// <summary>Clamps out-of-range values and returns one message per change (empty when nothing was wrong).</summary>
        public IList<string> Normalize()
        {
            var findings = new List<string>();
            WorkMinutes = Clamp(WorkMinutes, 1, 240, "workMinutes", findings);
            ShortBreakMinutes = Clamp(ShortBreakMinutes, 1, 120, "shortBreakMinutes", findings);
            LongBreakMinutes = Clamp(LongBreakMinutes, 1, 240, "longBreakMinutes", findings);
            SessionsBeforeLongBreak = Clamp(SessionsBeforeLongBreak, 1, 20, "sessionsBeforeLongBreak", findings);
            return findings;
        }

        internal TimeSpan DurationOf(FocusPhase phase)
        {
            switch (phase)
            {
                case FocusPhase.ShortBreak: return TimeSpan.FromMinutes(ShortBreakMinutes);
                case FocusPhase.LongBreak: return TimeSpan.FromMinutes(LongBreakMinutes);
                default: return TimeSpan.FromMinutes(WorkMinutes);
            }
        }

        private static int Clamp(int value, int min, int max, string name, List<string> findings)
        {
            if (value >= min && value <= max) return value;
            int clamped = Math.Max(min, Math.Min(max, value));
            findings.Add($"{name}: {value} is outside {min}..{max}; using {clamped}.");
            return clamped;
        }
    }

    /// <summary>Describes a phase that just ended (returned from <see cref="FocusTimerModel.Tick"/>).</summary>
    public sealed class FocusTransition
    {
        internal FocusTransition(FocusPhase completed, FocusPhase next, bool nextStarted, int workSessionsDone)
        {
            Completed = completed;
            Next = next;
            NextStarted = nextStarted;
            WorkSessionsDone = workSessionsDone;
        }

        public FocusPhase Completed { get; }
        public FocusPhase Next { get; }
        /// <summary>True when <see cref="FocusTimerOptions.AutoStartNext"/> started the next phase; false when the timer is waiting.</summary>
        public bool NextStarted { get; }
        public int WorkSessionsDone { get; }
    }

    /// <summary>
    /// A Pomodoro state machine. It never reads the clock: every call takes <c>now</c>, so it is fully testable.
    /// Not thread-safe; call it from one thread (the UI thread).
    /// Clock jumps: the remaining time is clamped to 0..phase length, so a clock moved backwards cannot extend a phase
    /// past its length and one moved forwards just finishes it. A late <see cref="Tick"/> (sleep, hibernate) completes
    /// one phase and starts the next from <c>now</c>, never a cascade of phases.
    /// </summary>
    public sealed class FocusTimerModel
    {
        private readonly FocusTimerOptions _options;
        private DateTimeOffset _endsAt;
        private TimeSpan _pausedRemaining;

        public FocusTimerModel(FocusTimerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Normalize();
            Phase = FocusPhase.Work;
            State = FocusState.Idle;
        }

        public FocusPhase Phase { get; private set; }
        public FocusState State { get; private set; }
        /// <summary>Work sessions that ran to the end (a skipped one does not count).</summary>
        public int WorkSessionsDone { get; private set; }

        public TimeSpan PhaseLength => _options.DurationOf(Phase);

        public TimeSpan Remaining(DateTimeOffset now)
        {
            TimeSpan length = PhaseLength;
            TimeSpan remaining;
            switch (State)
            {
                case FocusState.Running: remaining = _endsAt - now; break;
                case FocusState.Paused: remaining = _pausedRemaining; break;
                default: remaining = length; break;
            }

            if (remaining < TimeSpan.Zero) return TimeSpan.Zero;
            return remaining > length ? length : remaining;
        }

        /// <summary>Idle starts the current phase; Paused resumes; Running does nothing.</summary>
        public void Start(DateTimeOffset now)
        {
            if (State == FocusState.Running) return;
            TimeSpan remaining = State == FocusState.Paused ? _pausedRemaining : PhaseLength;
            _endsAt = now + remaining;
            State = FocusState.Running;
        }

        public void Pause(DateTimeOffset now)
        {
            if (State != FocusState.Running) return;
            _pausedRemaining = Remaining(now);
            State = FocusState.Paused;
        }

        /// <summary>Start/resume when not running, pause when running (what a click on the widget does).</summary>
        public void Toggle(DateTimeOffset now)
        {
            if (State == FocusState.Running) Pause(now);
            else Start(now);
        }

        /// <summary>Jumps to the next phase without counting the current one. The new phase is left Idle.</summary>
        public void Skip()
        {
            Phase = Phase == FocusPhase.Work ? ShortOrLongBreak(WorkSessionsDone) : FocusPhase.Work;
            State = FocusState.Idle;
        }

        /// <summary>Back to an idle first work session with the session count cleared.</summary>
        public void Reset()
        {
            Phase = FocusPhase.Work;
            State = FocusState.Idle;
            WorkSessionsDone = 0;
        }

        /// <summary>Call about once a second. Returns the transition when a phase just finished, otherwise null.</summary>
        public FocusTransition Tick(DateTimeOffset now)
        {
            if (State != FocusState.Running || Remaining(now) > TimeSpan.Zero) return null;

            FocusPhase completed = Phase;
            if (completed == FocusPhase.Work) WorkSessionsDone++;
            Phase = completed == FocusPhase.Work ? ShortOrLongBreak(WorkSessionsDone) : FocusPhase.Work;
            State = FocusState.Idle;
            if (_options.AutoStartNext) Start(now);
            return new FocusTransition(completed, Phase, _options.AutoStartNext, WorkSessionsDone);
        }

        private FocusPhase ShortOrLongBreak(int sessionsDone)
            => sessionsDone > 0 && sessionsDone % _options.SessionsBeforeLongBreak == 0 ? FocusPhase.LongBreak : FocusPhase.ShortBreak;

        /// <summary>The widget text: "Focus" when idle at the start, "Focus 24:59", "Break 04:12", "Focus 12:00 (paused)".</summary>
        public string FormatText(DateTimeOffset now)
        {
            string name = Phase == FocusPhase.Work ? "Focus" : "Break";
            if (State == FocusState.Idle && Phase == FocusPhase.Work && WorkSessionsDone == 0) return name;
            string time = FormatDuration(Remaining(now));
            if (State == FocusState.Idle) return $"{name} {time} (ready)";
            return State == FocusState.Paused ? $"{name} {time} (paused)" : $"{name} {time}";
        }

        /// <summary>The tooltip: phase, session count and the whole cycle.</summary>
        public string FormatToolTip(DateTimeOffset now)
        {
            string phase = Phase == FocusPhase.Work ? "Work" : Phase == FocusPhase.ShortBreak ? "Short break" : "Long break";
            return $"{phase}, {FormatDuration(Remaining(now))} left. Sessions done: {WorkSessionsDone}. Click to start or pause, right-click for more.";
        }

        /// <summary>mm:ss, or h:mm:ss from one hour. Seconds round up so a fresh 25 minute phase reads 25:00, not 24:59.</summary>
        public static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            long totalSeconds = (long)Math.Ceiling(span.TotalSeconds);
            long h = totalSeconds / 3600;
            long m = totalSeconds % 3600 / 60;
            long s = totalSeconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }
    }
}
