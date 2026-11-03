using System;
using System.Text;

namespace CairoDesktop.Features.ClipboardHistory
{
    /// <summary>One remembered clip. Immutable; "changes" produce a copy so snapshots can be shared across threads freely.</summary>
    public sealed class ClipboardEntry
    {
        public ClipboardEntry(long id, string text, ClipKind kind, DateTimeOffset createdAt, bool pinned, string sourceApp)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Id = id;
            Text = text;
            Kind = kind;
            CreatedAt = createdAt;
            Pinned = pinned;
            SourceApp = string.IsNullOrWhiteSpace(sourceApp) ? null : sourceApp;
            ByteSize = Utf8Size(text);
        }

        /// <summary>Unique within a store; never reused while the store lives.</summary>
        public long Id { get; }

        public string Text { get; }

        public ClipKind Kind { get; }

        /// <summary>When the text was (last) copied. Used for TTL only; list order is by recency of insertion.</summary>
        public DateTimeOffset CreatedAt { get; }

        public bool Pinned { get; }

        /// <summary>Process name that owned the clipboard, if known. Optional.</summary>
        public string SourceApp { get; }

        /// <summary>UTF-8 size of <see cref="Text"/>; this is what the byte cap counts.</summary>
        public int ByteSize { get; }

        public ClipboardEntry WithPinned(bool pinned) => pinned == Pinned ? this : new ClipboardEntry(Id, Text, Kind, CreatedAt, pinned, SourceApp);

        public ClipboardEntry WithCopied(DateTimeOffset createdAt, string sourceApp) => new ClipboardEntry(Id, Text, Kind, createdAt, Pinned, sourceApp ?? SourceApp);

        /// <summary>UTF-8 byte count; a lone surrogate counts as the 3-byte replacement character, as the encoder does.</summary>
        public static int Utf8Size(string text) => Encoding.UTF8.GetByteCount(text);

        /// <summary>A single-line preview of at most <paramref name="max"/> chars (never splits a surrogate pair).</summary>
        public string Preview(int max)
        {
            string flat = Text.Trim().Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            if (max < 1 || flat.Length <= max) return flat;
            int cut = max;
            if (char.IsHighSurrogate(flat[cut - 1])) cut--;
            return flat.Substring(0, cut) + "…";
        }
    }
}
