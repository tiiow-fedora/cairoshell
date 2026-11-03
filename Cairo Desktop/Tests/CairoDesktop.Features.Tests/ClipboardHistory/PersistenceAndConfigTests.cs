using System;
using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.ClipboardHistory;
using Xunit;

namespace CairoDesktop.Features.Tests.ClipboardHistory
{
    public class PersistenceTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));

        private static ClipboardEntry E(long id, string text, bool pinned = false, string app = null)
            => new ClipboardEntry(id, text, ClipKindDetector.Detect(text), T, pinned, app);

        [Fact]
        public void Serialize_ReturnsNullWhenPersistOff()
        {
            Assert.Null(HistoryPersistence.Serialize(new[] { E(1, "a") }, false));
        }

        [Fact]
        public void RoundTrip_PreservesEverything()
        {
            var src = new[] { E(5, "https://x.test", true, "chrome.exe"), E(6, "héllo \U0001F600\nline2") };
            var loaded = HistoryPersistence.Deserialize(HistoryPersistence.Serialize(src, true));
            Assert.Empty(loaded.Findings);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal(src[0].Text, loaded.Entries[0].Text);
            Assert.True(loaded.Entries[0].Pinned);
            Assert.Equal("chrome.exe", loaded.Entries[0].SourceApp);
            Assert.Equal(ClipKind.Url, loaded.Entries[0].Kind);
            Assert.Equal(T, loaded.Entries[0].CreatedAt);
            Assert.Equal(src[1].Text, loaded.Entries[1].Text);
            Assert.Equal(6, loaded.Entries[1].Id);
        }

        [Fact]
        public void Serialize_WritesVersionField()
        {
            string json = HistoryPersistence.Serialize(new ClipboardEntry[0], true);
            Assert.Contains("\"version\": 1", json);
        }

        [Fact]
        public void Serialize_DropsSensitiveEntries_NeverReachesDisk()
        {
            var policy = new SensitivePolicy(new SensitivePolicyOptions { ExcludedApps = new List<string> { "vault*" } });
            string json = HistoryPersistence.Serialize(new[]
            {
                E(1, "ok text"),
                E(2, "4111111111111111"),
                E(3, "AKIAIOSFODNN7EXAMPLE"),
                E(4, "harmless", false, "vault.exe"),
            }, true, policy);
            Assert.Contains("ok text", json);
            Assert.DoesNotContain("4111", json);
            Assert.DoesNotContain("AKIA", json);
            Assert.DoesNotContain("harmless", json);
        }

        [Fact]
        public void Deserialize_SecondLineOfDefence_SkipsSensitiveFromTamperedFile()
        {
            string json = "{\"version\":1,\"entries\":[{\"id\":1,\"text\":\"4111 1111 1111 1111\"},{\"id\":2,\"text\":\"fine\"}]}";
            var loaded = HistoryPersistence.Deserialize(json, new SensitivePolicy());
            Assert.Equal(new[] { "fine" }, loaded.Entries.Select(e => e.Text).ToArray());
            Assert.Contains(loaded.Findings, f => f.Contains("sensitive"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{not json")]
        [InlineData("[1,2,3]")]
        [InlineData("\"str\"")]
        [InlineData("{}")]
        [InlineData("{\"version\":\"1\",\"entries\":[]}")]
        [InlineData("{\"version\":1}")]
        [InlineData("{\"version\":1,\"entries\":{}}")]
        public void Deserialize_CorruptYieldsEmptyWithFinding(string json)
        {
            var r = HistoryPersistence.Deserialize(json);
            Assert.Empty(r.Entries);
            Assert.NotEmpty(r.Findings);
        }

        [Fact]
        public void Deserialize_TruncatedFileYieldsEmptyWithFinding()
        {
            string json = HistoryPersistence.Serialize(new[] { E(1, "abc") }, true);
            var r = HistoryPersistence.Deserialize(json.Substring(0, json.Length / 2));
            Assert.Empty(r.Entries);
            Assert.Contains(r.Findings, f => f.Contains("corrupt"));
        }

        [Fact]
        public void Deserialize_FutureVersionIsNotParsed()
        {
            var r = HistoryPersistence.Deserialize("{\"version\":2,\"entries\":[{\"id\":1,\"text\":\"a\"}]}");
            Assert.Empty(r.Entries);
            Assert.Contains(r.Findings, f => f.Contains("version 2"));
        }

        [Fact]
        public void Deserialize_BadEntriesSkippedGoodKept()
        {
            string json = "{\"version\":1,\"entries\":[7,{\"id\":1},{\"text\":\"no id\"},{\"id\":2,\"text\":\"  \"},{\"id\":3,\"text\":\"good\",\"createdAt\":\"nope\"},{\"id\":3,\"text\":\"dup\"}]}";
            var r = HistoryPersistence.Deserialize(json);
            Assert.Single(r.Entries);
            Assert.Equal("good", r.Entries[0].Text);
            Assert.True(r.Findings.Count >= 5);
        }

        [Fact]
        public void Deserialize_UnknownKindFallsBackToDetection()
        {
            var r = HistoryPersistence.Deserialize("{\"version\":1,\"entries\":[{\"id\":1,\"text\":\"https://a.test\",\"kind\":\"Banana\"}]}");
            Assert.Equal(ClipKind.Url, r.Entries[0].Kind);
        }

        [Fact]
        public void Deserialize_NeverThrowsOnWeirdTypes()
        {
            var r = HistoryPersistence.Deserialize("{\"version\":1,\"entries\":[{\"id\":\"x\",\"text\":5}]}");
            Assert.Empty(r.Entries);
        }

        [Fact]
        public void StoreLoad_AfterRoundTripKeepsOrder()
        {
            var store = new ClipboardHistoryStore();
            store.Add("one"); store.Add("two");
            string json = HistoryPersistence.Serialize(store.Snapshot(), true);
            var other = new ClipboardHistoryStore();
            other.Load(HistoryPersistence.Deserialize(json).Entries);
            Assert.Equal(new[] { "two", "one" }, other.Snapshot().Select(e => e.Text).ToArray());
        }

        // ---- atomic write ----

        [Fact]
        public void AtomicWrite_WritesTempThenReplaces()
        {
            var calls = new List<string>();
            string err = AtomicFile.Write("dir/h.json", "data",
                (p, c) => calls.Add("write:" + p + ":" + c),
                (a, b) => calls.Add("move:" + a + "->" + b));
            Assert.Null(err);
            Assert.Equal(new[] { "write:dir/h.json.tmp:data", "move:dir/h.json.tmp->dir/h.json" }, calls.ToArray());
        }

        [Fact]
        public void AtomicWrite_WriteFailureDoesNotReplaceAndCleansUp()
        {
            var calls = new List<string>();
            string err = AtomicFile.Write("h.json", "d",
                (p, c) => throw new System.IO.IOException("disk full"),
                (a, b) => calls.Add("move"),
                p => calls.Add("delete:" + p));
            Assert.Contains("disk full", err);
            Assert.Equal(new[] { "delete:h.json.tmp" }, calls.ToArray());
        }

        [Fact]
        public void AtomicWrite_ReplaceFailureReportedAndTempRemoved()
        {
            var deleted = new List<string>();
            string err = AtomicFile.Write("h.json", "d", (p, c) => { }, (a, b) => throw new System.IO.IOException("locked"), deleted.Add);
            Assert.Contains("locked", err);
            Assert.Single(deleted);
        }

        [Fact]
        public void AtomicWrite_CleanupFailureIsSwallowed()
        {
            string err = AtomicFile.Write("h.json", "d", (p, c) => throw new Exception("x"), (a, b) => { }, p => throw new Exception("y"));
            Assert.NotNull(err);
        }

        [Fact]
        public void WriteToDisk_RealFileSystemRoundTripAndOverwrite()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cairo-clip-" + Guid.NewGuid().ToString("N"));
            string path = System.IO.Path.Combine(dir, "sub", "history.json");
            try
            {
                Assert.Null(AtomicFile.WriteToDisk(path, "one"));
                Assert.Null(AtomicFile.WriteToDisk(path, "two"));
                Assert.Equal("two", System.IO.File.ReadAllText(path));
                Assert.False(System.IO.File.Exists(path + ".tmp"));
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }

    public class ConfigTests
    {
        [Fact]
        public void Defaults_AreConservative()
        {
            var c = new ClipboardConfig();
            Assert.False(c.Enabled);
            Assert.False(c.Persist);
            Assert.Contains("KeePass*", c.ExcludeApps);
        }

        [Fact]
        public void ParseDocument_MissingSectionGivesDefaultsNoFindings()
        {
            var r = ClipboardConfigParser.ParseDocument("{ \"theme\": \"x\" }");
            Assert.Empty(r.Findings);
            Assert.False(r.Config.Enabled);
        }

        [Fact]
        public void ParseDocument_ToleratesCommentsAndTrailingCommas()
        {
            var r = ClipboardConfigParser.ParseDocument("{ // c\n \"clipboardHistory\": { \"enabled\": true, }, }");
            Assert.Empty(r.Findings);
            Assert.True(r.Config.Enabled);
        }

        [Fact]
        public void ParseDocument_InvalidJsonGivesDefaultsAndFinding()
        {
            var r = ClipboardConfigParser.ParseDocument("{ nope");
            Assert.Single(r.Findings);
            Assert.True(r.Findings[0].IsError);
        }

        [Fact]
        public void ParseDocument_BlankIsDefaults()
        {
            Assert.Empty(ClipboardConfigParser.ParseDocument(null).Findings);
        }

        [Fact]
        public void Parse_AllFields()
        {
            var r = ClipboardConfigParser.ParseDocument(@"{ ""clipboardHistory"": {
                ""enabled"": true, ""maxItems"": 50, ""maxBytes"": 20000, ""ttlMinutes"": 30, ""persist"": true,
                ""excludeApps"": [""secret*"", ""foo""], ""pinnedMax"": 5, ""hotkey"": ""shift+ctrl+h"", ""previewLength"": 40 } }");
            Assert.Empty(r.Findings);
            var c = r.Config;
            Assert.True(c.Enabled); Assert.True(c.Persist);
            Assert.Equal(50, c.MaxItems); Assert.Equal(20000, c.MaxBytes); Assert.Equal(30, c.TtlMinutes);
            Assert.Equal(5, c.PinnedMax); Assert.Equal(40, c.PreviewLength);
            Assert.Equal("Ctrl+Shift+H", c.Hotkey);
            Assert.Equal(new[] { "secret*", "foo" }, c.ExcludeApps.ToArray());
        }

        [Fact]
        public void Parse_OutOfRangeIsClampedWithWarning()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"maxItems\":99999,\"previewLength\":1,\"ttlMinutes\":-5}}");
            Assert.Equal(1000, r.Config.MaxItems);
            Assert.Equal(10, r.Config.PreviewLength);
            Assert.Equal(0, r.Config.TtlMinutes);
            Assert.Equal(3, r.Findings.Count);
            Assert.All(r.Findings, f => Assert.False(f.IsError));
        }

        [Fact]
        public void Parse_WrongTypesFallBackWithErrorFindings()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"enabled\":\"yes\",\"maxItems\":\"many\",\"excludeApps\":\"x\",\"hotkey\":5}}");
            Assert.Equal(4, r.Findings.Count(f => f.IsError));
            Assert.False(r.Config.Enabled);
            Assert.Equal(100, r.Config.MaxItems);
            Assert.Equal("Ctrl+Alt+V", r.Config.Hotkey);
            Assert.Contains("KeePass*", r.Config.ExcludeApps);
        }

        [Fact]
        public void Parse_UnknownOptionIsWarningOnly()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"colour\":1}}");
            Assert.Single(r.Findings);
            Assert.Contains("colour", r.Findings[0].Path);
        }

        [Fact]
        public void Parse_SectionNotObject()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":[]}");
            Assert.Single(r.Findings);
        }

        [Fact]
        public void Parse_PinnedMaxCappedByMaxItems()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"maxItems\":5,\"pinnedMax\":30}}");
            Assert.Equal(5, r.Config.PinnedMax);
            Assert.Single(r.Findings);
        }

        [Fact]
        public void Parse_ExcludeAppsEmptyArrayMeansNoApps_BadEntriesIgnored()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"excludeApps\":[]}}");
            Assert.Empty(r.Config.ExcludeApps);
            var r2 = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"excludeApps\":[\"a\",1,\" \"]}}");
            Assert.Equal(new[] { "a" }, r2.Config.ExcludeApps.ToArray());
            Assert.Equal(2, r2.Findings.Count);
        }

        [Fact]
        public void Parse_NullHotkeyDisablesIt()
        {
            var r = ClipboardConfigParser.ParseDocument("{\"clipboardHistory\":{\"hotkey\":null}}");
            Assert.Null(r.Config.Hotkey);
            Assert.Empty(r.Findings);
        }

        [Theory]
        [InlineData("Ctrl+Alt+V", "Ctrl+Alt+V")]
        [InlineData("alt+ctrl+v", "Ctrl+Alt+V")]
        [InlineData("Win+Shift+F12", "Win+Shift+F12")]
        [InlineData("super+space", "Win+Space")]
        [InlineData("Ctrl+pagedown", "Ctrl+Pagedown")]
        public void Hotkey_Normalises(string input, string expected)
        {
            Assert.True(ClipboardConfigParser.TryNormalizeHotkey(input, out string n));
            Assert.Equal(expected, n);
        }

        [Theory]
        [InlineData("V")]
        [InlineData("Ctrl+")]
        [InlineData("Ctrl+Ctrl+V")]
        [InlineData("Ctrl+VV")]
        [InlineData("Ctrl+F25")]
        [InlineData("Banana+V")]
        [InlineData("")]
        [InlineData(null)]
        public void Hotkey_Rejects(string input)
        {
            Assert.False(ClipboardConfigParser.TryNormalizeHotkey(input, out _));
        }

        [Fact]
        public void ToStoreOptions_MapsTtlAndCapsPinned()
        {
            var c = new ClipboardConfig { MaxItems = 5, PinnedMax = 9, TtlMinutes = 0 };
            var o = c.ToStoreOptions();
            Assert.Null(o.Ttl);
            Assert.Equal(5, o.MaxPinned);
            c.TtlMinutes = 10;
            Assert.Equal(TimeSpan.FromMinutes(10), c.ToStoreOptions().Ttl);
        }
    }
}
