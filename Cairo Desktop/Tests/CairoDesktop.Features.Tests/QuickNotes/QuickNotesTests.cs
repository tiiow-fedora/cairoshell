using System;
using CairoDesktop.Features.QuickNotes;
using Xunit;

namespace CairoDesktop.Features.Tests.QuickNotes
{
    public class QuickNotesTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero);
        private static QuickNotesModel Make(Action<QuickNotesOptions> tweak = null)
        {
            var o = new QuickNotesOptions();
            tweak?.Invoke(o);
            o.Normalize();
            return new QuickNotesModel(o);
        }

        [Fact]
        public void Empty_shows_label_and_hint()
        {
            var m = Make();
            Assert.Equal("Notes", m.FormatText());
            Assert.Contains("Click to add", m.FormatToolTip());
        }

        [Fact]
        public void Add_trims_flattens_and_counts()
        {
            var m = Make();
            Assert.True(m.Add("  buy milk\r\nand eggs\t ", T0));
            Assert.Equal("buy milk and eggs", m.Notes[0].Text);
            Assert.Equal("Notes 1", m.FormatText());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  \r\n\t ")]
        public void Blank_is_rejected(string text)
        {
            var m = Make();
            Assert.False(m.Add(text, T0));
            Assert.Equal(0, m.Count);
        }

        [Fact]
        public void Oldest_dropped_beyond_max()
        {
            var m = Make(o => o.MaxNotes = 2);
            m.Add("a", T0); m.Add("b", T0); m.Add("c", T0);
            Assert.Equal(2, m.Count);
            Assert.Equal("b", m.Notes[0].Text);
            Assert.Equal("c", m.Notes[1].Text);
        }

        [Fact]
        public void Long_note_is_cut()
        {
            var m = Make(o => o.MaxLength = 10);
            m.Add(new string('x', 50), T0);
            Assert.Equal(10, m.Notes[0].Text.Length);
        }

        [Fact]
        public void Tooltip_is_newest_first_and_counts_hidden()
        {
            var m = Make(o => o.ToolTipNotes = 2);
            m.Add("one", T0); m.Add("two", T0.AddMinutes(1)); m.Add("three", T0.AddMinutes(2));
            string tip = m.FormatToolTip();
            Assert.True(tip.IndexOf("three") < tip.IndexOf("two"));
            Assert.DoesNotContain("one", tip.Replace("(+1 older)", ""));
            Assert.Contains("(+1 older)", tip);
        }

        [Fact]
        public void RemoveAt_and_Clear()
        {
            var m = Make();
            m.Add("a", T0); m.Add("b", T0);
            Assert.False(m.RemoveAt(5));
            Assert.True(m.RemoveAt(0));
            Assert.Equal("b", m.Notes[0].Text);
            m.Clear();
            Assert.Equal(0, m.Count);
        }

        [Fact]
        public void Serialize_round_trips_and_skips_bad_lines()
        {
            var m = Make();
            m.Add("first", T0); m.Add("second: with\ttab", T0.AddHours(1));
            string text = m.Serialize();
            var m2 = Make();
            Assert.Equal(0, m2.Load(text));
            Assert.Equal(2, m2.Count);
            Assert.Equal("first", m2.Notes[0].Text);
            Assert.Equal(T0.AddHours(1), m2.Notes[1].Created);
            Assert.Equal(2, m2.Load(text + "garbage line\nnot-a-date\ttext\n"));
            Assert.Equal(2, m2.Count);
        }

        [Fact]
        public void Normalize_clamps()
        {
            var o = new QuickNotesOptions { MaxNotes = 0, MaxLength = 1, ToolTipNotes = 99, Label = " " };
            var findings = o.Normalize();
            Assert.Equal(3, findings.Count);
            Assert.Equal(1, o.MaxNotes);
            Assert.Equal("Notes", o.Label);
        }
    }
}
