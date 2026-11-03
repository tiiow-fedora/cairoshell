using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.ClipboardHistory;
using Xunit;

namespace CairoDesktop.Features.Tests.ClipboardHistory
{
    /// <summary>Regressions found in the adversarial review of the clipboard module.</summary>
    public class ReviewRegressionTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private static ClipboardEntry E(long id, string text, bool pinned = false, DateTimeOffset? at = null)
            => new ClipboardEntry(id, text, ClipKindDetector.Detect(text), at ?? T, pinned, null);

        private static IEnumerable<ClipboardEntry> Throwing()
        {
            yield return E(99, "x");
            throw new InvalidOperationException("boom");
        }

        [Fact]
        public void Serialize_LoneSurrogate_DoesNotThrowAndRoundTrips()
        {
            string json = HistoryPersistence.Serialize(new[] { E(1, "bad \uD800 text") }, true);
            Assert.NotNull(json);
            var loaded = HistoryPersistence.Deserialize(json);
            Assert.Single(loaded.Entries);
            Assert.Contains("bad", loaded.Entries[0].Text);
        }

        [Fact]
        public void Deserialize_LoneSurrogateEscape_DoesNotThrow()
        {
            string json = "{\"version\":1,\"entries\":[{\"id\":1,\"text\":\"a\\ud800b\"},{\"id\":2,\"text\":\"ok\"}]}";
            var loaded = HistoryPersistence.Deserialize(json);
            Assert.Contains(loaded.Entries, e => e.Text == "ok");
        }

        [Fact]
        public void Serialize_WithoutPolicy_StillDropsSecrets()
        {
            string json = HistoryPersistence.Serialize(new[] { E(1, "4111 1111 1111 1111"), E(2, "fine") }, true);
            Assert.DoesNotContain("4111", json);
            Assert.Contains("fine", json);
        }

        [Fact]
        public void Deserialize_WithoutPolicy_StillDropsSecrets()
        {
            string json = "{\"version\":1,\"entries\":[{\"id\":1,\"text\":\"4111 1111 1111 1111\"},{\"id\":2,\"text\":\"fine\"}]}";
            var loaded = HistoryPersistence.Deserialize(json);
            Assert.Single(loaded.Entries);
        }

        [Theory]
        [InlineData("card 4111 1111 1111 1111 12/25")]
        [InlineData("4111 1111 1111 1111 123")]
        [InlineData("4111\u00A01111\u00A01111\u00A01111")]
        [InlineData("4111\u202F1111\u202F1111\u202F1111")]
        public void Card_AdjacentDigitsOrNbsp_StillDetected(string text)
        {
            Assert.True(new SensitivePolicy().LooksSensitive(text));
        }

        [Fact]
        public void TitleCase_CombiningMarkDoesNotStartNewWord()
        {
            Assert.Equal("Cafe\u0301s", ClipTransforms.TitleCase("cafe\u0301s").Output);
        }

        [Fact]
        public void JsonPretty_LoneSurrogateEscape_DoesNotThrow()
        {
            TransformResult r = ClipTransforms.JsonPretty("{\"a\":\"\\ud800\"}");
            Assert.NotNull(r);
        }

        [Fact]
        public void Load_PinnedBytesOverCap_AreDemotedSoByteCapHolds()
        {
            var store = new ClipboardHistoryStore(new ClipboardStoreOptions { MaxBytes = 10, MaxItems = 10, MaxPinned = 10 });
            store.Load(new[] { E(1, "aaaaaaaa", true), E(2, "bbbbbbbb", true) });
            Assert.True(store.TotalBytes <= 10, "bytes " + store.TotalBytes);
        }

        [Fact]
        public void Load_PinnedAboveMaxItems_CountCapHolds()
        {
            var store = new ClipboardHistoryStore(new ClipboardStoreOptions { MaxItems = 2, MaxPinned = 5 });
            store.Load(new[] { E(1, "a", true), E(2, "b", true), E(3, "c", true) });
            Assert.True(store.Count <= 2);
        }

        [Fact]
        public void Load_FutureTimestamp_StillExpires()
        {
            DateTimeOffset now = T;
            var store = new ClipboardHistoryStore(new ClipboardStoreOptions { Ttl = TimeSpan.FromMinutes(10) }, () => now);
            store.Load(new[] { E(1, "from the future", false, T.AddYears(50)) });
            now = T.AddMinutes(11);
            store.Expire();
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void Load_ThrowingEnumerable_LeavesExistingContents()
        {
            var store = new ClipboardHistoryStore();
            store.Add("keep me");
            Assert.Throws<InvalidOperationException>(() => store.Load(Throwing()));
            Assert.Equal("keep me", store.Snapshot().Single().Text);
        }
    }
}
