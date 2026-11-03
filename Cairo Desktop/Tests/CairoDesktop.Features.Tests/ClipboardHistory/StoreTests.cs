using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CairoDesktop.Features.ClipboardHistory;
using Xunit;

namespace CairoDesktop.Features.Tests.ClipboardHistory
{
    public class StoreTests
    {
        private sealed class Clock
        {
            public DateTimeOffset Now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
            public DateTimeOffset Read() => Now;
        }

        private static ClipboardHistoryStore Make(Clock clock, Action<ClipboardStoreOptions> tweak = null)
        {
            var o = new ClipboardStoreOptions();
            tweak?.Invoke(o);
            return new ClipboardHistoryStore(o, clock.Read);
        }

        private static async Task<bool> Finishes(Task t, int seconds = 5)
        {
            return await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(seconds))) == t;
        }

        private static string[] Texts(ClipboardHistoryStore s) => s.Snapshot().Select(e => e.Text).ToArray();

        [Fact]
        public void Add_StoresNewestFirst()
        {
            var s = Make(new Clock());
            s.Add("a"); s.Add("b"); s.Add("c");
            Assert.Equal(new[] { "c", "b", "a" }, Texts(s));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t ")]
        [InlineData(null)]
        public void Add_IgnoresBlank(string text)
        {
            var s = Make(new Clock());
            Assert.Equal(AddStatus.Ignored, s.Add(text).Status);
            Assert.Equal(0, s.Count);
        }

        [Fact]
        public void Trim_PolicyTrimRemovesBothEnds()
        {
            var s = Make(new Clock(), o => o.Trim = TrimPolicy.Trim);
            s.Add("  hi  ");
            Assert.Equal("hi", Texts(s)[0]);
        }

        [Fact]
        public void Trim_PolicyTrimEndKeepsIndent()
        {
            var s = Make(new Clock(), o => o.Trim = TrimPolicy.TrimEnd);
            s.Add("  hi  \n");
            Assert.Equal("  hi", Texts(s)[0]);
        }

        [Fact]
        public void Trim_PolicyNoneKeepsAll()
        {
            var s = Make(new Clock(), o => o.Trim = TrimPolicy.None);
            s.Add(" x ");
            Assert.Equal(" x ", Texts(s)[0]);
        }

        [Fact]
        public void Dedupe_MovesToTopAndReturnsMoved()
        {
            var s = Make(new Clock());
            s.Add("a"); s.Add("b");
            var r = s.Add("a");
            Assert.Equal(AddStatus.Moved, r.Status);
            Assert.Equal(new[] { "a", "b" }, Texts(s));
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void Dedupe_KeepsPinnedStateAndId()
        {
            var s = Make(new Clock());
            long id = s.Add("keep").Entry.Id;
            s.Pin(id);
            s.Add("other");
            var r = s.Add("keep");
            Assert.True(r.Entry.Pinned);
            Assert.Equal(id, r.Entry.Id);
        }

        [Fact]
        public void Dedupe_RefreshesCreatedAtSoItSurvivesTtl()
        {
            var c = new Clock();
            var s = Make(c, o => o.Ttl = TimeSpan.FromMinutes(10));
            s.Add("x");
            c.Now += TimeSpan.FromMinutes(9);
            s.Add("x");
            c.Now += TimeSpan.FromMinutes(9);
            Assert.Equal(0, s.Expire());
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public void Dedupe_IsCaseSensitive()
        {
            var s = Make(new Clock());
            s.Add("Abc"); s.Add("abc");
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void Dedupe_AfterTrimTreatsPaddedTextAsSame()
        {
            var s = Make(new Clock());
            s.Add("x");
            Assert.Equal(AddStatus.Moved, s.Add("  x \n").Status);
        }

        [Fact]
        public void MaxItems_EvictsOldestUnpinned()
        {
            var s = Make(new Clock(), o => o.MaxItems = 3);
            for (int i = 1; i <= 5; i++) s.Add("t" + i);
            Assert.Equal(new[] { "t5", "t4", "t3" }, Texts(s));
        }

        [Fact]
        public void MaxItems_PinnedOldestIsSkippedDuringEviction()
        {
            var s = Make(new Clock(), o => o.MaxItems = 3);
            long first = s.Add("t1").Entry.Id;
            s.Pin(first);
            s.Add("t2"); s.Add("t3"); s.Add("t4");
            // t1 is oldest but pinned; t2 (oldest unpinned) goes.
            Assert.Equal(new[] { "t1", "t4", "t3" }, Texts(s));
        }

        [Fact]
        public void MaxItems_AllPinnedFullRejectsNewClip()
        {
            var s = Make(new Clock(), o => { o.MaxItems = 2; o.MaxPinned = 2; });
            s.Add("a"); s.Add("b");
            s.Pin(s.Snapshot()[0].Id); s.Pin(s.Snapshot()[1].Id);
            var r = s.Add("c");
            Assert.Equal(AddStatus.Rejected, r.Status);
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void MaxBytes_EvictsOldestUntilUnderCap()
        {
            var s = Make(new Clock(), o => o.MaxBytes = 10);
            s.Add("aaaa"); s.Add("bbbb"); s.Add("cccc");
            Assert.Equal(new[] { "cccc", "bbbb" }, Texts(s));
            Assert.Equal(8, s.TotalBytes);
        }

        [Fact]
        public void MaxBytes_CountsUtf8NotChars()
        {
            var s = Make(new Clock(), o => o.MaxBytes = 6);
            s.Add("€€"); // 2 chars, 6 bytes
            Assert.Equal(6, s.TotalBytes);
            s.Add("€");
            Assert.Equal(new[] { "€" }, Texts(s));
        }

        [Fact]
        public void MaxBytes_OversizedEntryRejectedWithoutEvictingOthers()
        {
            var s = Make(new Clock(), o => o.MaxBytes = 5);
            s.Add("abc");
            var r = s.Add("123456789");
            Assert.Equal(AddStatus.Rejected, r.Status);
            Assert.Null(r.Entry);
            Assert.Equal(new[] { "abc" }, Texts(s));
        }

        [Fact]
        public void MaxBytes_PinnedBytesReduceRoomForNewEntries()
        {
            var s = Make(new Clock(), o => o.MaxBytes = 10);
            s.Pin(s.Add("123456").Entry.Id);
            Assert.Equal(AddStatus.Rejected, s.Add("12345").Status);
            Assert.Equal(AddStatus.Added, s.Add("1234").Status);
        }

        [Fact]
        public void Pin_RespectsMaxPinned()
        {
            var s = Make(new Clock(), o => o.MaxPinned = 2);
            var a = s.Add("a").Entry.Id; var b = s.Add("b").Entry.Id; var c = s.Add("c").Entry.Id;
            Assert.True(s.Pin(a));
            Assert.True(s.Pin(b));
            Assert.False(s.Pin(c));
            Assert.True(s.Pin(a)); // already pinned is not an error
        }

        [Fact]
        public void Pin_UnknownIdReturnsFalse()
        {
            var s = Make(new Clock());
            Assert.False(s.Pin(999));
            Assert.False(s.Unpin(999));
        }

        [Fact]
        public void Unpin_AllowsLaterEviction()
        {
            var s = Make(new Clock(), o => o.MaxItems = 2);
            long a = s.Add("a").Entry.Id;
            s.Pin(a);
            s.Add("b"); s.Add("c");
            Assert.Equal(new[] { "a", "c" }, Texts(s));
            s.Unpin(a);
            s.Add("d");
            Assert.Equal(new[] { "d", "c" }, Texts(s));
        }

        [Fact]
        public void Ordering_PinnedFirstThenNewest()
        {
            var s = Make(new Clock());
            long a = s.Add("a").Entry.Id; s.Add("b"); long c = s.Add("c").Entry.Id; s.Add("d");
            s.Pin(a); s.Pin(c);
            Assert.Equal(new[] { "c", "a", "d", "b" }, Texts(s));
        }

        [Fact]
        public void Remove_DropsEntryAndBytes()
        {
            var s = Make(new Clock());
            long id = s.Add("abc").Entry.Id;
            Assert.True(s.Remove(id));
            Assert.False(s.Remove(id));
            Assert.Equal(0, s.TotalBytes);
        }

        [Fact]
        public void Clear_KeepPinnedByDefault()
        {
            var s = Make(new Clock());
            long a = s.Add("a").Entry.Id; s.Add("b");
            s.Pin(a);
            Assert.Equal(1, s.Clear());
            Assert.Equal(new[] { "a" }, Texts(s));
        }

        [Fact]
        public void Clear_CanRemovePinnedToo()
        {
            var s = Make(new Clock());
            s.Pin(s.Add("a").Entry.Id);
            Assert.Equal(1, s.Clear(false));
            Assert.Equal(0, s.Count);
            Assert.Equal(0, s.TotalBytes);
        }

        [Fact]
        public void Ttl_BoundaryExactlyTtlExpires()
        {
            var c = new Clock();
            var s = Make(c, o => o.Ttl = TimeSpan.FromMinutes(5));
            s.Add("x");
            c.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1);
            Assert.Equal(0, s.Expire());
            c.Now += TimeSpan.FromTicks(1);
            Assert.Equal(1, s.Expire());
        }

        [Fact]
        public void Ttl_NeverExpiresPinned()
        {
            var c = new Clock();
            var s = Make(c, o => o.Ttl = TimeSpan.FromMinutes(1));
            s.Pin(s.Add("p").Entry.Id);
            s.Add("u");
            c.Now += TimeSpan.FromHours(5);
            Assert.Equal(1, s.Expire());
            Assert.Equal(new[] { "p" }, Texts(s));
        }

        [Fact]
        public void Ttl_NullMeansNever()
        {
            var c = new Clock();
            var s = Make(c);
            s.Add("x");
            c.Now += TimeSpan.FromDays(3650);
            Assert.Equal(0, s.Expire());
        }

        [Fact]
        public void Ttl_ClockRegressionDoesNotExpireAnything()
        {
            var c = new Clock();
            var s = Make(c, o => o.Ttl = TimeSpan.FromMinutes(1));
            s.Add("x");
            c.Now -= TimeSpan.FromDays(2);
            Assert.Equal(0, s.Expire());
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public void Ttl_AddExpiresStaleBeforeInserting()
        {
            var c = new Clock();
            var s = Make(c, o => o.Ttl = TimeSpan.FromMinutes(1));
            s.Add("old");
            c.Now += TimeSpan.FromMinutes(2);
            s.Add("new");
            Assert.Equal(new[] { "new" }, Texts(s));
        }

        [Fact]
        public void ClockRegression_OrderingStillByInsertion()
        {
            var c = new Clock();
            var s = Make(c);
            s.Add("first");
            c.Now -= TimeSpan.FromHours(1);
            s.Add("second");
            Assert.Equal(new[] { "second", "first" }, Texts(s));
        }

        [Fact]
        public void Search_CaseInsensitiveSubstring()
        {
            var s = Make(new Clock());
            s.Add("Hello World"); s.Add("nothing");
            Assert.Equal(new[] { "Hello World" }, s.Search("WORLD", null).Select(e => e.Text).ToArray());
        }

        [Fact]
        public void Search_KindFilter()
        {
            var s = Make(new Clock());
            s.Add("https://example.com"); s.Add("plain text");
            var r = s.Search(null, ClipKind.Url);
            Assert.Single(r);
            Assert.Equal(ClipKind.Url, r[0].Kind);
        }

        [Fact]
        public void Search_BlankQueryReturnsAllInDisplayOrder()
        {
            var s = Make(new Clock());
            long a = s.Add("a").Entry.Id; s.Add("b");
            s.Pin(a);
            Assert.Equal(new[] { "a", "b" }, s.Search("  ", null).Select(e => e.Text).ToArray());
        }

        [Fact]
        public void Search_NoMatchIsEmptyNotNull()
        {
            var s = Make(new Clock());
            s.Add("a");
            Assert.Empty(s.Search("zzz", null));
        }

        [Fact]
        public void Snapshot_IsImmutableCopy()
        {
            var s = Make(new Clock());
            s.Add("a");
            var snap = s.Snapshot();
            s.Add("b");
            Assert.Single(snap);
            Assert.IsNotType<List<ClipboardEntry>>(snap);
        }

        [Fact]
        public void Ids_AreUniqueAndNotReusedAfterRemove()
        {
            var s = Make(new Clock());
            long a = s.Add("a").Entry.Id;
            s.Remove(a);
            long b = s.Add("b").Entry.Id;
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Load_AppliesCapsAndAdvancesIds()
        {
            var s = Make(new Clock(), o => { o.MaxItems = 2; o.MaxPinned = 1; });
            var t = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            s.Load(new[]
            {
                new ClipboardEntry(10, "a", ClipKind.Plain, t, true, null),
                new ClipboardEntry(11, "b", ClipKind.Plain, t, true, null),   // second pin exceeds MaxPinned
                new ClipboardEntry(12, "c", ClipKind.Plain, t, false, null),
            });
            Assert.Equal(2, s.Count);
            Assert.Equal(1, s.Snapshot().Count(e => e.Pinned));
            Assert.True(s.Add("new").Entry.Id > 12);
        }

        [Fact]
        public void Load_SkipsDuplicateIdsAndTexts()
        {
            var s = Make(new Clock());
            var t = DateTimeOffset.UtcNow;
            s.Load(new[]
            {
                new ClipboardEntry(1, "a", ClipKind.Plain, t, false, null),
                new ClipboardEntry(1, "b", ClipKind.Plain, t, false, null),
                new ClipboardEntry(2, "a", ClipKind.Plain, t, false, null),
            });
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public void Options_AreNormalisedToSaneMinimums()
        {
            var s = new ClipboardHistoryStore(new ClipboardStoreOptions { MaxItems = 0, MaxBytes = -4, MaxPinned = -1 });
            Assert.Equal(1, s.Options.MaxItems);
            Assert.Equal(0, s.Options.MaxPinned);
            Assert.True(s.Options.MaxBytes >= 1);
        }

        // ---- events, re-entrancy, concurrency ----

        [Fact]
        public void Changed_RaisedWithKindAndIncreasingVersion()
        {
            var s = Make(new Clock());
            var seen = new List<HistoryChangedEventArgs>();
            s.Changed += (o, e) => seen.Add(e);
            long id = s.Add("a").Entry.Id;
            s.Pin(id);
            s.Remove(id);
            Assert.Equal(new[] { HistoryChangeKind.Added, HistoryChangeKind.Pinned, HistoryChangeKind.Removed }, seen.Select(e => e.Kind).ToArray());
            Assert.True(seen[0].Version < seen[1].Version && seen[1].Version < seen[2].Version);
        }

        [Fact]
        public void Changed_NotRaisedForIgnoredOrNoopOperations()
        {
            var s = Make(new Clock());
            int n = 0;
            s.Changed += (o, e) => n++;
            s.Add("  ");
            s.Remove(5);
            s.Clear();
            s.Expire();
            Assert.Equal(0, n);
        }

        [Fact]
        public void Changed_HandlerMayCallBackIntoStore()
        {
            var s = Make(new Clock());
            int snapshotCount = -1;
            s.Changed += (o, e) => { snapshotCount = s.Snapshot().Count; };
            s.Add("a");
            Assert.Equal(1, snapshotCount);
        }

        [Fact]
        public async Task Changed_HandlerThatMutatesDoesNotDeadlockOrRecurseForever()
        {
            var s = Make(new Clock());
            int depth = 0;
            s.Changed += (o, e) =>
            {
                if (e.Kind == HistoryChangeKind.Added && depth++ < 3) s.Add("echo" + depth);
            };
            var t = Task.Run(() => s.Add("seed"));
            Assert.True(await Finishes(t), "store deadlocked when a handler re-entered it");
            Assert.True(s.Count >= 2);
        }

        [Fact]
        public async Task Changed_HandlerBlockingOnAnotherThreadThatUsesStoreDoesNotDeadlock()
        {
            // Models Dispatcher.Invoke from a handler while the other thread needs the store lock.
            var s = Make(new Clock());
            s.Changed += (o, e) =>
            {
                int count = Task.Run(() => s.Count).GetAwaiter().GetResult();
                Assert.True(count >= 0);
            };
            var t = Task.Run(() => s.Add("x"));
            Assert.True(await Finishes(t), "handler ran inside the store lock");
        }

        [Fact]
        public void Changed_ThrowingHandlerDoesNotBreakAddOrOtherHandlers()
        {
            var s = Make(new Clock());
            bool second = false;
            s.Changed += (o, e) => throw new InvalidOperationException("boom");
            s.Changed += (o, e) => second = true;
            var r = s.Add("a");
            Assert.Equal(AddStatus.Added, r.Status);
            Assert.True(second);
            Assert.Equal(1, s.Count);
        }

        [Fact]
        public async Task Concurrent_AddsKeepInvariants()
        {
            var s = Make(new Clock(), o => { o.MaxItems = 50; o.MaxBytes = 400; o.MaxPinned = 5; });
            int threads = 8, perThread = 400;
            var start = new ManualResetEventSlim();
            var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                start.Wait();
                var rnd = new Random(t);
                for (int i = 0; i < perThread; i++)
                {
                    var r = s.Add("item-" + rnd.Next(0, 120));
                    if (r.Entry != null && i % 17 == 0) s.Pin(r.Entry.Id);
                    if (i % 29 == 0) s.Remove(rnd.Next(1, 200));
                    if (i % 41 == 0) s.Snapshot();
                }
            })).ToArray();
            start.Set();
            Assert.True(await Finishes(Task.WhenAll(tasks), 30));

            var snap = s.Snapshot();
            Assert.True(snap.Count <= 50);
            Assert.Equal(snap.Sum(e => (long)e.ByteSize), s.TotalBytes);
            Assert.True(s.TotalBytes <= 400);
            Assert.Equal(snap.Count, snap.Select(e => e.Id).Distinct().Count());
            Assert.Equal(snap.Count, snap.Select(e => e.Text).Distinct().Count());
            Assert.True(snap.Count(e => e.Pinned) <= 5);
            // pinned entries come first
            bool seenUnpinned = false;
            foreach (var e in snap)
            {
                if (!e.Pinned) seenUnpinned = true;
                else Assert.False(seenUnpinned);
            }
        }

        [Fact]
        public void Concurrent_EventVersionsAreUnique()
        {
            var s = Make(new Clock(), o => o.MaxItems = 1000);
            var versions = new System.Collections.Concurrent.ConcurrentBag<long>();
            s.Changed += (o, e) => versions.Add(e.Version);
            Parallel.For(0, 500, i => s.Add("v" + i));
            Assert.Equal(500, versions.Count);
            Assert.Equal(500, versions.Distinct().Count());
        }
    }
}
