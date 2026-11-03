using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>Builds the opaque string that identifies a window in <see cref="SnapMemory"/>.</summary>
    public static class WindowKey
    {
        /// <summary>
        /// "hwnd:process:titleHash". The process name guards against a recycled window handle; the title is optional
        /// (a window that retitles itself, like a browser tab, would otherwise lose its memory). Never parsed back.
        /// </summary>
        public static string Make(long hwnd, string processName, string title = null)
        {
            return hwnd.ToString("x") + ":" + (processName ?? "").Trim().ToLowerInvariant() + ":" + Hash(title);
        }

        /// <summary>FNV-1a, stable across runs and runtimes (string.GetHashCode is not).</summary>
        internal static string Hash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "0";
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in text)
                {
                    h = (h ^ c) * 16777619;
                }

                return h.ToString("x");
            }
        }
    }

    /// <summary>Why a window should not be snapped.</summary>
    [Flags]
    public enum WindowStates
    {
        Normal = 0,
        Maximized = 1,
        Minimized = 2,
        FullScreen = 4,
        /// <summary>Window that can't or shouldn't be moved (tool window, fixed-size dialog, our own shell windows...).</summary>
        Excluded = 8
    }

    /// <summary>Decides which window states are snappable.</summary>
    public sealed class SnapPolicy
    {
        /// <summary>When true a maximized window is restored first and then snapped; when false it is left alone.</summary>
        public bool SnapMaximized { get; set; }

        /// <summary>Minimized windows are never snapped (SetWindowPos on them corrupts the restore rectangle).</summary>
        public bool SnapMinimized => false;

        public bool CanSnap(WindowStates state)
        {
            if ((state & (WindowStates.Minimized | WindowStates.FullScreen | WindowStates.Excluded)) != 0) return false;
            if ((state & WindowStates.Maximized) != 0 && !SnapMaximized) return false;
            return true;
        }
    }

    /// <summary>
    /// Remembers each snapped window's rectangle from before it was snapped so "restore" can put it back.
    /// Bounded: the least recently used entry is dropped when <see cref="Capacity"/> is exceeded. Holds no handles or timers;
    /// call <see cref="Prune"/> with an IsWindow check to drop windows that have gone away. Not thread-safe (UI thread only).
    /// </summary>
    public sealed class SnapMemory
    {
        public sealed class Entry
        {
            public string Key { get; internal set; }
            /// <summary>The window rectangle before the first snap.</summary>
            public Rect OriginalRect { get; internal set; }
            /// <summary>The rectangle the window had right after we snapped it (used to notice that the user moved it since).</summary>
            public Rect AppliedRect { get; internal set; }
            public int ZoneIndex { get; internal set; }
            public string LayoutId { get; internal set; }
        }

        /// <summary>Tolerance when deciding whether a window is still where we put it (windows enforce minimum sizes).</summary>
        public const int StillSnappedTolerance = 2;

        private readonly Dictionary<string, LinkedListNode<Entry>> _map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _order = new LinkedList<Entry>(); // first = most recently used

        public SnapMemory(int capacity = 64)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Capacity { get; }
        public int Count => _map.Count;

        /// <summary>Keys, most recently used first.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (Entry e in _order) yield return e.Key;
            }
        }

        /// <summary>
        /// Records a snap. <paramref name="currentRect"/> is the window rectangle just before this snap.
        /// If the window is still sitting where the previous snap put it, the original rectangle is kept (zone-to-zone moves
        /// must not overwrite it); otherwise <paramref name="currentRect"/> becomes the new original.
        /// Returns the key of the entry evicted to make room, or null.
        /// </summary>
        public string Remember(string key, Rect currentRect, Rect appliedRect, int zoneIndex, string layoutId)
        {
            if (key == null) return null;

            if (_map.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                Entry e = node.Value;
                if (!currentRect.NearlyEquals(e.AppliedRect, StillSnappedTolerance)) e.OriginalRect = currentRect;
                e.AppliedRect = appliedRect;
                e.ZoneIndex = zoneIndex;
                e.LayoutId = layoutId;
                Touch(node);
                return null;
            }

            var entry = new Entry { Key = key, OriginalRect = currentRect, AppliedRect = appliedRect, ZoneIndex = zoneIndex, LayoutId = layoutId };
            _map[key] = _order.AddFirst(entry);

            if (_map.Count <= Capacity) return null;

            Entry oldest = _order.Last.Value;
            _order.RemoveLast();
            _map.Remove(oldest.Key);
            return oldest.Key;
        }

        /// <summary>Looks up an entry and marks it recently used.</summary>
        public bool TryGet(string key, out Entry entry)
        {
            entry = null;
            if (key == null || !_map.TryGetValue(key, out LinkedListNode<Entry> node)) return false;
            Touch(node);
            entry = node.Value;
            return true;
        }

        /// <summary>Looks up an entry without changing the eviction order.</summary>
        public bool Peek(string key, out Entry entry)
        {
            entry = null;
            if (key == null || !_map.TryGetValue(key, out LinkedListNode<Entry> node)) return false;
            entry = node.Value;
            return true;
        }

        /// <summary>True when the window is known and is still (within tolerance) at the rectangle we gave it.</summary>
        public bool IsStillSnapped(string key, Rect currentRect)
        {
            return Peek(key, out Entry e) && currentRect.NearlyEquals(e.AppliedRect, StillSnappedTolerance);
        }

        /// <summary>Returns the pre-snap rectangle and forgets the window. False if the window was never snapped (or was evicted).</summary>
        public bool Restore(string key, out Rect original)
        {
            original = default;
            if (!Peek(key, out Entry e)) return false;
            original = e.OriginalRect;
            Forget(key);
            return true;
        }

        /// <summary>Drops a window (it closed, or the user moved it manually). Returns false if it wasn't known.</summary>
        public bool Forget(string key)
        {
            if (key == null || !_map.TryGetValue(key, out LinkedListNode<Entry> node)) return false;
            _order.Remove(node);
            _map.Remove(key);
            return true;
        }

        /// <summary>Drops every entry for which <paramref name="isAlive"/> returns false (e.g. IsWindow). Returns how many were dropped.</summary>
        public int Prune(Func<string, bool> isAlive)
        {
            if (isAlive == null) return 0;
            var dead = new List<string>();
            foreach (Entry e in _order)
            {
                bool alive;
                try { alive = isAlive(e.Key); }
                catch { alive = false; }
                if (!alive) dead.Add(e.Key);
            }

            foreach (string key in dead) Forget(key);
            return dead.Count;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }

        private void Touch(LinkedListNode<Entry> node)
        {
            if (node == _order.First) return;
            _order.Remove(node);
            _order.AddFirst(node);
        }
    }
}
