using System;
using CairoDesktop.Features.FocusTimer;
using Xunit;

namespace CairoDesktop.Features.Tests.FocusTimer
{
    public class FocusTimerTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        private static FocusTimerModel Make(bool auto = false, int sessions = 4)
            => new FocusTimerModel(new FocusTimerOptions { WorkMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, SessionsBeforeLongBreak = sessions, AutoStartNext = auto });

        [Fact]
        public void Idle_by_default_shows_plain_label()
        {
            var m = Make();
            Assert.Equal(FocusState.Idle, m.State);
            Assert.Equal("Focus", m.FormatText(T0));
            Assert.Equal(TimeSpan.FromMinutes(25), m.Remaining(T0));
        }

        [Fact]
        public void Start_counts_down_and_formats_with_round_up()
        {
            var m = Make();
            m.Start(T0);
            Assert.Equal("Focus 25:00", m.FormatText(T0));
            Assert.Equal("Focus 24:59", m.FormatText(T0.AddMilliseconds(1000)));
            Assert.Equal("Focus 25:00", m.FormatText(T0.AddMilliseconds(500)));
            Assert.Equal("Focus 24:59", m.FormatText(T0.AddMilliseconds(1500)));
            Assert.Equal("Focus 00:01", m.FormatText(T0.AddMinutes(25).AddMilliseconds(-100)));
        }

        [Fact]
        public void Pause_freezes_and_resume_continues()
        {
            var m = Make();
            m.Start(T0);
            m.Pause(T0.AddMinutes(10));
            Assert.Equal(FocusState.Paused, m.State);
            Assert.Equal("Focus 15:00 (paused)", m.FormatText(T0.AddHours(5)));
            m.Start(T0.AddHours(5));
            Assert.Equal(TimeSpan.FromMinutes(15), m.Remaining(T0.AddHours(5)));
            Assert.Equal(TimeSpan.FromMinutes(10), m.Remaining(T0.AddHours(5).AddMinutes(5)));
        }

        [Fact]
        public void Toggle_flips_between_running_and_paused()
        {
            var m = Make();
            m.Toggle(T0);
            Assert.Equal(FocusState.Running, m.State);
            m.Toggle(T0.AddSeconds(5));
            Assert.Equal(FocusState.Paused, m.State);
            m.Toggle(T0.AddSeconds(9));
            Assert.Equal(FocusState.Running, m.State);
        }

        [Fact]
        public void Tick_before_the_end_does_nothing()
        {
            var m = Make();
            m.Start(T0);
            Assert.Null(m.Tick(T0.AddMinutes(24)));
            Assert.Equal(FocusState.Running, m.State);
        }

        [Fact]
        public void Finishing_work_moves_to_a_waiting_short_break()
        {
            var m = Make();
            m.Start(T0);
            FocusTransition t = m.Tick(T0.AddMinutes(25));
            Assert.NotNull(t);
            Assert.Equal(FocusPhase.Work, t.Completed);
            Assert.Equal(FocusPhase.ShortBreak, t.Next);
            Assert.False(t.NextStarted);
            Assert.Equal(1, t.WorkSessionsDone);
            Assert.Equal(FocusState.Idle, m.State);
            Assert.Equal("Break 05:00 (ready)", m.FormatText(T0.AddMinutes(26)));
        }

        [Fact]
        public void Fourth_session_earns_a_long_break_then_the_cycle_restarts()
        {
            var m = Make(auto: true);
            var now = T0;
            m.Start(now);
            var phases = new System.Collections.Generic.List<FocusPhase>();
            for (int i = 0; i < 8; i++)
            {
                now = now.AddMinutes(30);
                FocusTransition t = m.Tick(now);
                Assert.NotNull(t);
                phases.Add(t.Completed);
                phases.Add(t.Next);
            }

            Assert.Equal(new[]
            {
                FocusPhase.Work, FocusPhase.ShortBreak, FocusPhase.ShortBreak, FocusPhase.Work,
                FocusPhase.Work, FocusPhase.ShortBreak, FocusPhase.ShortBreak, FocusPhase.Work,
                FocusPhase.Work, FocusPhase.ShortBreak, FocusPhase.ShortBreak, FocusPhase.Work,
                FocusPhase.Work, FocusPhase.LongBreak, FocusPhase.LongBreak, FocusPhase.Work,
            }, phases.ToArray());
        }

        [Fact]
        public void Auto_start_begins_the_next_phase_from_now()
        {
            var m = Make(auto: true);
            m.Start(T0);
            FocusTransition t = m.Tick(T0.AddMinutes(25));
            Assert.True(t.NextStarted);
            Assert.Equal(FocusState.Running, m.State);
            Assert.Equal(TimeSpan.FromMinutes(5), m.Remaining(T0.AddMinutes(25)));
        }

        [Fact]
        public void A_very_late_tick_completes_one_phase_not_a_cascade()
        {
            var m = Make(auto: true);
            m.Start(T0);
            FocusTransition t = m.Tick(T0.AddHours(10));
            Assert.Equal(FocusPhase.Work, t.Completed);
            Assert.Equal(FocusPhase.ShortBreak, m.Phase);
            Assert.Equal(TimeSpan.FromMinutes(5), m.Remaining(T0.AddHours(10)));
            Assert.Null(m.Tick(T0.AddHours(10)));
        }

        [Fact]
        public void Skip_does_not_count_the_session_and_leaves_the_timer_idle()
        {
            var m = Make();
            m.Start(T0);
            m.Skip();
            Assert.Equal(FocusPhase.ShortBreak, m.Phase);
            Assert.Equal(0, m.WorkSessionsDone);
            Assert.Equal(FocusState.Idle, m.State);
            m.Skip();
            Assert.Equal(FocusPhase.Work, m.Phase);
        }

        [Fact]
        public void Reset_clears_everything()
        {
            var m = Make();
            m.Start(T0);
            m.Tick(T0.AddMinutes(25));
            m.Reset();
            Assert.Equal(FocusPhase.Work, m.Phase);
            Assert.Equal(0, m.WorkSessionsDone);
            Assert.Equal("Focus", m.FormatText(T0.AddHours(1)));
        }

        [Fact]
        public void Clock_moved_backwards_cannot_extend_a_phase()
        {
            var m = Make();
            m.Start(T0);
            Assert.Equal(TimeSpan.FromMinutes(25), m.Remaining(T0.AddHours(-3)));
        }

        [Fact]
        public void Pausing_when_not_running_is_ignored()
        {
            var m = Make();
            m.Pause(T0);
            Assert.Equal(FocusState.Idle, m.State);
        }

        [Fact]
        public void Options_are_clamped_with_a_message_each()
        {
            var o = new FocusTimerOptions { WorkMinutes = 0, ShortBreakMinutes = 999, LongBreakMinutes = 15, SessionsBeforeLongBreak = -2 };
            var findings = o.Normalize();
            Assert.Equal(3, findings.Count);
            Assert.Equal(1, o.WorkMinutes);
            Assert.Equal(120, o.ShortBreakMinutes);
            Assert.Equal(1, o.SessionsBeforeLongBreak);
            Assert.Empty(new FocusTimerOptions().Normalize());
        }

        [Theory]
        [InlineData(0, "00:00")]
        [InlineData(61, "01:01")]
        [InlineData(3599, "59:59")]
        [InlineData(3600, "1:00:00")]
        [InlineData(-5, "00:00")]
        public void FormatDuration_cases(int seconds, string expected)
            => Assert.Equal(expected, FocusTimerModel.FormatDuration(TimeSpan.FromSeconds(seconds)));

        [Fact]
        public void Long_work_phase_shows_hours()
        {
            var m = new FocusTimerModel(new FocusTimerOptions { WorkMinutes = 90 });
            m.Start(T0);
            Assert.Equal("Focus 1:30:00", m.FormatText(T0));
        }
    }
}
