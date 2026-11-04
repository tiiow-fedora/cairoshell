using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CairoDesktop.Features.QuickNotes
{
    public sealed class QuickNote
    {
        public QuickNote(string text, DateTimeOffset created)
        {
            Text = text;
            Created = created;
        }

        public string Text { get; }
        public DateTimeOffset Created { get; }
    }

    public sealed class QuickNotesOptions
    {
        /// <summary>Oldest notes are dropped beyond this many.</summary>
        public int MaxNotes { get; set; } = 50;
        /// <summary>A note longer than this is cut.</summary>
        public int MaxLength { get; set; } = 500;
        /// <summary>Notes listed in the tooltip, newest first.</summary>
        public int ToolTipNotes { get; set; } = 8;
        public string Label { get; set; } = "Notes";

        public IList<string> Normalize()
        {
            var findings = new List<string>();
            MaxNotes = Clamp(MaxNotes, 1, 1000, "maxNotes", findings);
            MaxLength = Clamp(MaxLength, 10, 5000, "maxLength", findings);
            ToolTipNotes = Clamp(ToolTipNotes, 1, 30, "toolTipNotes", findings);
            if (string.IsNullOrWhiteSpace(Label)) Label = "Notes";
            return findings;
        }

        private static int Clamp(int value, int min, int max, string name, IList<string> findings)
        {
            if (value >= min && value <= max) return value;
            int clamped = value < min ? min : max;
            findings.Add(name + ": " + value + " is outside " + min + ".." + max + "; using " + clamped + ".");
            return clamped;
        }
    }

    /// <summary>A short list of text notes, newest last. No clock and no file access: pass <c>now</c>, and save through <see cref="Serialize"/>.</summary>
    public sealed class QuickNotesModel
    {
        private readonly QuickNotesOptions _options;
        private readonly List<QuickNote> _notes = new List<QuickNote>();

        public QuickNotesModel(QuickNotesOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IReadOnlyList<QuickNote> Notes => _notes;
        public int Count => _notes.Count;

        /// <summary>Adds a note (trimmed, line breaks become spaces, cut to MaxLength). Returns false for blank text.</summary>
        public bool Add(string text, DateTimeOffset now)
        {
            string clean = Clean(text);
            if (clean.Length == 0) return false;
            if (clean.Length > _options.MaxLength) clean = clean.Substring(0, _options.MaxLength);
            _notes.Add(new QuickNote(clean, now));
            while (_notes.Count > _options.MaxNotes) _notes.RemoveAt(0);
            return true;
        }

        /// <summary>Removes the note at <paramref name="index"/> (0 = oldest). False when out of range.</summary>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _notes.Count) return false;
            _notes.RemoveAt(index);
            return true;
        }

        public void Clear() => _notes.Clear();

        public string FormatText() => _notes.Count == 0 ? _options.Label : _options.Label + " " + _notes.Count;

        /// <summary>Newest first, one line each: "02-03 10:00  text". Empty list gives a hint.</summary>
        public string FormatToolTip()
        {
            if (_notes.Count == 0) return "No notes. Click to add one.";
            var sb = new StringBuilder();
            foreach (QuickNote n in Enumerable.Reverse(_notes).Take(_options.ToolTipNotes))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(n.Created.ToString("MM-dd HH:mm")).Append("  ").Append(n.Text);
            }

            int hidden = _notes.Count - _options.ToolTipNotes;
            if (hidden > 0) sb.Append("\n(+").Append(hidden).Append(" older)");
            return sb.ToString();
        }

        /// <summary>One note per line: "ISO-8601 time TAB text". Notes never contain line breaks or tabs-before-text issues, so this round-trips.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (QuickNote n in _notes)
                sb.Append(n.Created.ToString("o", System.Globalization.CultureInfo.InvariantCulture)).Append('\t').Append(n.Text).Append('\n');
            return sb.ToString();
        }

        /// <summary>Replaces the list from <see cref="Serialize"/> output. Bad lines are skipped; returns how many were.</summary>
        public int Load(string text)
        {
            _notes.Clear();
            int skipped = 0;
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string line in text.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || !DateTimeOffset.TryParse(line.Substring(0, tab), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset when))
                {
                    skipped++;
                    continue;
                }

                string body = Clean(line.Substring(tab + 1));
                if (body.Length == 0) { skipped++; continue; }
                _notes.Add(new QuickNote(body, when));
            }

            while (_notes.Count > _options.MaxNotes) _notes.RemoveAt(0);
            return skipped;
        }

        private static string Clean(string text)
        {
            if (text == null) return "";
            return string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
