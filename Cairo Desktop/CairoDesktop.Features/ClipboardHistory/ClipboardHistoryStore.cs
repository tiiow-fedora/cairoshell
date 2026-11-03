using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CairoDesktop.Features.ClipboardHistory
{
    /// <summary>How text is normalised before it is stored (and compared for duplicates).</summary>
    public enum TrimPolicy
    {
        /// <summary>Keep the text exactly as copied (whitespace-only is still ignored).</summary>
        None,
        /// <summary>Remove leading and trailing whitespace.</summary>
        Trim,
        /// <summary>Remove trailing whitespace only (keeps indentation of copied code).</summary>
        TrimEnd
    }

    public sealed class ClipboardStoreOptions
    {
        public int MaxItems { get; set; } = 100;

        /// <summary>Cap on the total UTF-8 bytes of all entries (pinned included).</summary>
        public long MaxBytes { get; set; } = 5L * 1024 * 1024;

        /// <summary>Pinned entries are never evicted, so they are capped separately.</summary>
        public int MaxPinned { get; set; } = 20;

        /// <summary>Unpinned entries older than this are dropped by <see cref="ClipboardHistoryStore.Expire"/>. Null = never.</summary>
        public TimeSpan? Ttl { get; set; }

        public TrimPolicy Trim { get; set; } = TrimPolicy.Trim;
    }

    public enum AddStatus
    {
        /// <summary>A new entry was stored.</summary>
        Added,
        /// <summary>The same text already existed; it moved to the top (pinned state kept).</summary>
        Moved,
        /// <summary>Empty or whitespace-only; nothing stored.</summary>
        Ignored,
        /// <summary>Does not fit under the byte/count caps even after evicting every unpinned entry.</summary>
        Rejected
    }

    public sealed class AddResult
    {
        internal AddResult(AddStatus status, ClipboardEntry entry) { Status = status; Entry = entry; }
        public AddStatus Status { get; }
        public ClipboardEntry Entry { get; }
    }

    public enum HistoryChangeKind { Added, Moved, Pinned, Unpinned, Removed, Cleared, Expired, Loaded }

    public sealed class HistoryChangedEventArgs : EventArgs
    {
        internal HistoryChangedEventArgs(HistoryChangeKind kind, long version)
        {
            Kind = kind;
            Version = version;
        }

        public HistoryChangeKind Kind { get; }

        /// <summary>Increases by one per mutation. Events from concurrent threads may be delivered out of order; ignore a Version lower than one you have already rendered.</summary>
        public long Version { get; }
    }

    /// <summary>
    /// In-memory clipboard history. Thread-safe.
    /// <para>
    /// Locking: every mutation happens under one private lock and returns immutable snapshots. The
    /// <see cref="Changed"/> event is raised only after the lock is released. Raising it inside the lock would
    /// deadlock as soon as a handler marshals to the UI thread (Dispatcher.Invoke) while the UI thread is itself
    /// waiting for the lock, and would let a handler re-enter the store and observe half-applied state.
    /// Handlers may therefore freely call back into the store.
    /// </para>
    /// </summary>
    public sealed class ClipboardHistoryStore
    {
        private readonly object _gate = new object();
        private readonly Func<DateTimeOffset> _clock;
        private readonly ClipboardStoreOptions _options;
        private readonly List<ClipboardEntry> _items = new List<ClipboardEntry>(); // index 0 = most recently copied
        private long _nextId = 1;
        private long _version;
        private long _bytes;

        public ClipboardHistoryStore(ClipboardStoreOptions options = null, Func<DateTimeOffset> clock = null)
        {
            _options = Normalize(options ?? new ClipboardStoreOptions());
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public event EventHandler<HistoryChangedEventArgs> Changed;

        public ClipboardStoreOptions Options => _options;

        public int Count { get { lock (_gate) return _items.Count; } }

        public long TotalBytes { get { lock (_gate) return _bytes; } }

        public long Version { get { lock (_gate) return _version; } }

        private static ClipboardStoreOptions Normalize(ClipboardStoreOptions o)
        {
            return new ClipboardStoreOptions
            {
                MaxItems = Math.Max(1, o.MaxItems),
                MaxBytes = Math.Max(1, o.MaxBytes),
                MaxPinned = Math.Max(0, o.MaxPinned),
                Ttl = o.Ttl,
                Trim = o.Trim
            };
        }

        private string Prepare(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            switch (_options.Trim)
            {
                case TrimPolicy.Trim: return text.Trim();
                case TrimPolicy.TrimEnd: return text.TrimEnd();
                default: return text;
            }
        }

        /// <summary>Records a clip. Dedupes identical text by moving it to the top.</summary>
        public AddResult Add(string text, string sourceApp = null)
        {
            string prepared = Prepare(text);
            if (prepared == null) return new AddResult(AddStatus.Ignored, null);

            AddResult result;
            HistoryChangedEventArgs evt = null;
            bool expiredSomething = false;
            lock (_gate)
            {
                DateTimeOffset now = _clock();
                int before = _items.Count;
                ExpireLocked(now);
                expiredSomething = _items.Count != before;

                int existing = _items.FindIndex(e => string.Equals(e.Text, prepared, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    ClipboardEntry moved = _items[existing].WithCopied(now, sourceApp);
                    _items.RemoveAt(existing);
                    _items.Insert(0, moved);
                    result = new AddResult(AddStatus.Moved, moved);
                    evt = Bump(HistoryChangeKind.Moved);
                }
                else
                {
                    int newBytes = ClipboardEntry.Utf8Size(prepared);
                    long pinnedBytes = 0;
                    int pinnedCount = 0;
                    foreach (ClipboardEntry e in _items)
                    {
                        if (!e.Pinned) continue;
                        pinnedBytes += e.ByteSize;
                        pinnedCount++;
                    }

                    // Check up front so a clip that can never fit does not evict innocent entries for nothing.
                    if (pinnedBytes + newBytes > _options.MaxBytes || pinnedCount + 1 > _options.MaxItems)
                    {
                        result = new AddResult(AddStatus.Rejected, null);
                        if (expiredSomething) evt = Bump(HistoryChangeKind.Expired);
                    }
                    else
                    {
                        var entry = new ClipboardEntry(_nextId++, prepared, ClipKindDetector.Detect(prepared), now, false, sourceApp);
                        _items.Insert(0, entry);
                        _bytes = SumBytes();
                        EvictLocked();
                        result = new AddResult(AddStatus.Added, entry);
                        evt = Bump(HistoryChangeKind.Added);
                    }
                }
            }

            Raise(evt);
            return result;
        }

        public bool Pin(long id) => SetPinned(id, true);

        public bool Unpin(long id) => SetPinned(id, false);

        /// <summary>Pins/unpins. Returns false if the id is unknown or the pinned cap would be exceeded.</summary>
        public bool SetPinned(long id, bool pinned)
        {
            HistoryChangedEventArgs evt;
            lock (_gate)
            {
                int i = _items.FindIndex(e => e.Id == id);
                if (i < 0) return false;
                ClipboardEntry e0 = _items[i];
                if (e0.Pinned == pinned) return true;
                if (pinned && _items.Count(e => e.Pinned) >= _options.MaxPinned) return false;
                _items[i] = e0.WithPinned(pinned);
                if (!pinned) EvictLocked(); // an unpinned entry becomes evictable, so caps may now bite
                evt = Bump(pinned ? HistoryChangeKind.Pinned : HistoryChangeKind.Unpinned);
            }

            Raise(evt);
            return true;
        }

        public bool Remove(long id)
        {
            HistoryChangedEventArgs evt;
            lock (_gate)
            {
                int i = _items.FindIndex(e => e.Id == id);
                if (i < 0) return false;
                _items.RemoveAt(i);
                _bytes = SumBytes();
                evt = Bump(HistoryChangeKind.Removed);
            }

            Raise(evt);
            return true;
        }

        /// <summary>Removes everything (or everything except pinned entries). Returns how many were removed.</summary>
        public int Clear(bool keepPinned = true)
        {
            HistoryChangedEventArgs evt = null;
            int removed;
            lock (_gate)
            {
                removed = _items.RemoveAll(e => !(keepPinned && e.Pinned));
                if (removed > 0)
                {
                    _bytes = SumBytes();
                    evt = Bump(HistoryChangeKind.Cleared);
                }
            }

            Raise(evt);
            return removed;
        }

        /// <summary>Drops unpinned entries older than the TTL. A clock that moved backwards never expires anything (age is clamped to zero).</summary>
        public int Expire()
        {
            HistoryChangedEventArgs evt = null;
            int removed;
            lock (_gate)
            {
                int before = _items.Count;
                ExpireLocked(_clock());
                removed = before - _items.Count;
                if (removed > 0) evt = Bump(HistoryChangeKind.Expired);
            }

            Raise(evt);
            return removed;
        }

        /// <summary>Replaces the contents with restored entries (applies caps; the newest-first order given is kept).</summary>
        public void Load(IEnumerable<ClipboardEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            HistoryChangedEventArgs evt;
            lock (_gate)
            {
                _items.Clear();
                int pinned = 0;
                var seen = new HashSet<long>();
                var seenText = new HashSet<string>(StringComparer.Ordinal);
                foreach (ClipboardEntry e in entries)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Text) || !seen.Add(e.Id) || !seenText.Add(e.Text)) continue;
                    ClipboardEntry use = e;
                    if (use.Pinned && ++pinned > _options.MaxPinned) use = use.WithPinned(false);
                    _items.Add(use);
                    if (use.Id >= _nextId) _nextId = use.Id + 1;
                }

                _bytes = SumBytes();
                ExpireLocked(_clock());
                EvictLocked();
                evt = Bump(HistoryChangeKind.Loaded);
            }

            Raise(evt);
        }

        public ClipboardEntry Get(long id)
        {
            lock (_gate) return _items.FirstOrDefault(e => e.Id == id);
        }

        /// <summary>Immutable copy, pinned first then newest first.</summary>
        public IReadOnlyList<ClipboardEntry> Snapshot() => Search(null, null);

        /// <summary>Case-insensitive substring search plus optional kind filter, in display order. Blank query matches all.</summary>
        public IReadOnlyList<ClipboardEntry> Search(string query, ClipKind? kind)
        {
            ClipboardEntry[] copy;
            lock (_gate) copy = _items.ToArray();

            IEnumerable<ClipboardEntry> q = copy;
            if (kind.HasValue) q = q.Where(e => e.Kind == kind.Value);
            if (!string.IsNullOrWhiteSpace(query))
            {
                string needle = query.Trim();
                q = q.Where(e => e.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Stable: LINQ OrderBy keeps recency order inside each group.
            return new ReadOnlyCollection<ClipboardEntry>(q.OrderBy(e => e.Pinned ? 0 : 1).ToList());
        }

        // ---- helpers; all *Locked methods require _gate to be held ----

        private HistoryChangedEventArgs Bump(HistoryChangeKind kind) => new HistoryChangedEventArgs(kind, ++_version);

        private long SumBytes()
        {
            long sum = 0;
            foreach (ClipboardEntry e in _items) sum += e.ByteSize;
            return sum;
        }

        private void ExpireLocked(DateTimeOffset now)
        {
            if (_options.Ttl == null) return;
            TimeSpan ttl = _options.Ttl.Value;
            int removed = _items.RemoveAll(e =>
            {
                if (e.Pinned) return false;
                TimeSpan age = now - e.CreatedAt;
                if (age < TimeSpan.Zero) age = TimeSpan.Zero; // clock went backwards: do not mass-expire
                return age >= ttl;
            });
            if (removed > 0) _bytes = SumBytes();
        }

        private void EvictLocked()
        {
            // _items is newest-first, so the oldest unpinned entry is the last unpinned one.
            while (_items.Count > _options.MaxItems || _bytes > _options.MaxBytes)
            {
                int victim = _items.FindLastIndex(e => !e.Pinned);
                if (victim < 0) break; // only pinned entries remain; they are never evicted
                _bytes -= _items[victim].ByteSize;
                _items.RemoveAt(victim);
            }
        }

        private void Raise(HistoryChangedEventArgs evt)
        {
            if (evt == null) return;
            // Outside the lock on purpose (see class remarks). A throwing handler must not break the caller's add.
            EventHandler<HistoryChangedEventArgs> handler = Changed;
            if (handler == null) return;
            foreach (EventHandler<HistoryChangedEventArgs> h in handler.GetInvocationList())
            {
                try { h(this, evt); }
                catch (Exception) { /* a subscriber bug must not corrupt or abort clipboard capture */ }
            }
        }
    }
}
