using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>
    /// Remembers what the user launches. Each entry keeps a score that grows by 1 per launch and decays
    /// exponentially with a half-life (default 14 days), so a launch from a month ago counts for about a quarter.
    /// The clock is injected; if it moves backwards the elapsed time is treated as zero (no boost, no negative decay).
    /// The store is pruned to <see cref="MaxEntries"/> by dropping the entries with the lowest current score.
    /// Thread-safety: all members take an internal lock and are safe from any thread.
    /// </summary>
    public sealed class FrecencyStore
    {
        public const int DefaultMaxEntries = 500;
        public static readonly TimeSpan DefaultHalfLife = TimeSpan.FromDays(14);
        private const int MaxIdLength = 512;

        private sealed class Entry
        {
            public double Score;
            public DateTimeOffset Last;
            public int Count;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Func<DateTimeOffset> _clock;
        private readonly double _halfLifeSeconds;

        public FrecencyStore(Func<DateTimeOffset> clock, int maxEntries = DefaultMaxEntries, TimeSpan? halfLife = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            MaxEntries = Math.Max(1, maxEntries);
            TimeSpan hl = halfLife ?? DefaultHalfLife;
            if (hl <= TimeSpan.Zero) hl = DefaultHalfLife;
            _halfLifeSeconds = hl.TotalSeconds;
        }

        public int MaxEntries { get; }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>Records one launch of <paramref name="id"/>. Null, empty and over-long ids are ignored.</summary>
        public void Record(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return;
            DateTimeOffset now = _clock();

            lock (_lock)
            {
                if (_entries.TryGetValue(id, out Entry e))
                {
                    e.Score = Decayed(e, now) + 1.0;
                    e.Last = now;
                    e.Count++;
                }
                else
                {
                    _entries[id] = new Entry { Score = 1.0, Last = now, Count = 1 };
                }

                Prune(now);
            }
        }

        /// <summary>Current decayed score of <paramref name="id"/>; 0 when unknown.</summary>
        public double GetScore(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            DateTimeOffset now = _clock();
            lock (_lock)
            {
                return _entries.TryGetValue(id, out Entry e) ? Decayed(e, now) : 0;
            }
        }

        /// <summary>Total number of recorded launches of <paramref name="id"/>.</summary>
        public int GetCount(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            lock (_lock)
            {
                return _entries.TryGetValue(id, out Entry e) ? e.Count : 0;
            }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        private double Decayed(Entry e, DateTimeOffset now)
        {
            double seconds = (now - e.Last).TotalSeconds;
            if (seconds <= 0 || double.IsNaN(seconds)) return e.Score; // clock moved backwards
            return e.Score * Math.Pow(0.5, seconds / _halfLifeSeconds);
        }

        private void Prune(DateTimeOffset now)
        {
            if (_entries.Count <= MaxEntries) return;

            var victims = _entries
                .Select(kv => new { kv.Key, Score = Decayed(kv.Value, now), kv.Value.Last })
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Last)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(_entries.Count - MaxEntries)
                .ToList();

            foreach (var v in victims) _entries.Remove(v.Key);
        }

        /// <summary>Serialises the store to a JSON string (stable order: highest score first, then id).</summary>
        public string ToJson()
        {
            DateTimeOffset now = _clock();
            List<KeyValuePair<string, Entry>> snapshot;
            lock (_lock)
            {
                snapshot = _entries.Select(kv => new KeyValuePair<string, Entry>(kv.Key,
                    new Entry { Score = Decayed(kv.Value, now), Last = kv.Value.Last, Count = kv.Value.Count })).ToList();
            }

            snapshot = snapshot.OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();

            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();
                    w.WriteNumber("version", 1);
                    w.WriteStartArray("entries");
                    foreach (var kv in snapshot)
                    {
                        w.WriteStartObject();
                        w.WriteString("id", kv.Key);
                        // The score is stored as of "now" with "last" = now so loading needs no extra decay maths.
                        w.WriteNumber("score", kv.Value.Score);
                        w.WriteString("last", now.ToString("o", CultureInfo.InvariantCulture));
                        w.WriteNumber("count", kv.Value.Count);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Builds a store from JSON written by <see cref="ToJson"/>. Corrupt or empty input yields an empty store;
        /// bad entries are skipped; every problem is added to <paramref name="findings"/> (may be null). Never throws.
        /// </summary>
        public static FrecencyStore FromJson(string json, Func<DateTimeOffset> clock, IList<string> findings = null,
            int maxEntries = DefaultMaxEntries, TimeSpan? halfLife = null)
        {
            var store = new FrecencyStore(clock, maxEntries, halfLife);
            if (string.IsNullOrWhiteSpace(json)) return store;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
                    {
                        findings?.Add("frecency data has no \"entries\" array; starting empty.");
                        return store;
                    }

                    int index = 0;
                    foreach (JsonElement el in entries.EnumerateArray())
                    {
                        index++;
                        if (!TryReadEntry(el, out string id, out Entry entry))
                        {
                            findings?.Add($"frecency entry {index} is invalid and was skipped.");
                            continue;
                        }

                        store._entries[id] = entry;
                    }

                    lock (store._lock) store.Prune(clock());
                }
            }
            catch (JsonException ex)
            {
                store.Clear();
                findings?.Add("frecency data is corrupt (" + ex.Message + "); starting empty.");
            }
            catch (InvalidOperationException ex)
            {
                store.Clear();
                findings?.Add("frecency data is corrupt (" + ex.Message + "); starting empty.");
            }

            return store;
        }

        private static bool TryReadEntry(JsonElement el, out string id, out Entry entry)
        {
            id = null;
            entry = null;
            if (el.ValueKind != JsonValueKind.Object) return false;
            if (!el.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.String) return false;
            id = idEl.GetString();
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
            if (!el.TryGetProperty("score", out JsonElement scoreEl) || scoreEl.ValueKind != JsonValueKind.Number || !scoreEl.TryGetDouble(out double score)) return false;
            if (double.IsNaN(score) || double.IsInfinity(score) || score < 0 || score > 1e9) return false;
            if (!el.TryGetProperty("last", out JsonElement lastEl) || lastEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(lastEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset last)) return false;

            int count = 1;
            if (el.TryGetProperty("count", out JsonElement countEl) && countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt32(out int c) && c > 0) count = c;

            entry = new Entry { Score = score, Last = last, Count = count };
            return true;
        }
    }
}
