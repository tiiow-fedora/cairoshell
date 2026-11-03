using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CairoDesktop.Features.ClipboardHistory
{
    public sealed class LoadedHistory
    {
        internal LoadedHistory(List<ClipboardEntry> entries, List<string> findings)
        {
            Entries = entries;
            Findings = findings;
        }

        /// <summary>Newest first, as saved.</summary>
        public IReadOnlyList<ClipboardEntry> Entries { get; }

        /// <summary>Human-readable problems; empty when the file was clean.</summary>
        public IReadOnlyList<string> Findings { get; }
    }

    /// <summary>
    /// Versioned JSON persistence. Privacy rules enforced here, not left to callers:
    /// nothing is serialised unless <c>persist</c> is true, and (when a policy is supplied) entries that the sensitive
    /// policy rejects are dropped on save and again on load, so a file edited by hand or written by an older build
    /// cannot smuggle a secret back into the list.
    /// </summary>
    public static class HistoryPersistence
    {
        public const int CurrentVersion = 1;

        private static readonly SensitivePolicy DefaultPolicy = new SensitivePolicy();

        /// <summary>Returns the file content, or null when <paramref name="persist"/> is false (nothing may be written).</summary>
        public static string Serialize(IEnumerable<ClipboardEntry> entries, bool persist, SensitivePolicy policy = null)
        {
            if (!persist) return null;
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            policy = policy ?? DefaultPolicy; // secrets must never reach disk, even if a caller forgot to pass a policy

            using (var ms = new MemoryStream())
            {
                var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                using (var w = new Utf8JsonWriter(ms, options))
                {
                    w.WriteStartObject();
                    w.WriteNumber("version", CurrentVersion);
                    w.WriteStartArray("entries");
                    foreach (ClipboardEntry e in entries)
                    {
                        if (e == null) continue;
                        if (policy != null && (policy.LooksSensitive(e.Text) || policy.IsExcludedApp(e.SourceApp))) continue;
                        w.WriteStartObject();
                        w.WriteNumber("id", e.Id);
                        w.WriteString("text", e.Text);
                        w.WriteString("kind", e.Kind.ToString());
                        w.WriteString("createdAt", e.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
                        w.WriteBoolean("pinned", e.Pinned);
                        if (e.SourceApp != null) w.WriteString("sourceApp", e.SourceApp);
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Parses a saved file. Never throws: corrupt or newer-version content yields no entries plus a finding.</summary>
        public static LoadedHistory Deserialize(string json, SensitivePolicy policy = null)
        {
            var entries = new List<ClipboardEntry>();
            var findings = new List<string>();
            policy = policy ?? DefaultPolicy;
            if (string.IsNullOrWhiteSpace(json))
            {
                findings.Add("History file is empty.");
                return new LoadedHistory(entries, findings);
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        findings.Add("History file is not a JSON object; starting empty.");
                        return new LoadedHistory(entries, findings);
                    }

                    if (!root.TryGetProperty("version", out JsonElement ver) || ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out int version))
                    {
                        findings.Add("History file has no valid version; starting empty.");
                        return new LoadedHistory(entries, findings);
                    }

                    if (version > CurrentVersion || version < 1)
                    {
                        findings.Add($"History file version {version} is not supported (this build reads up to {CurrentVersion}); starting empty and leaving the file untouched until the next save.");
                        return new LoadedHistory(entries, findings);
                    }

                    if (!root.TryGetProperty("entries", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
                    {
                        findings.Add("History file has no 'entries' array; starting empty.");
                        return new LoadedHistory(entries, findings);
                    }

                    var ids = new HashSet<long>();
                    int index = -1;
                    foreach (JsonElement item in arr.EnumerateArray())
                    {
                        index++;
                        ClipboardEntry e;
                        try { e = ReadEntry(item, index, findings); }
                        catch (InvalidOperationException) { findings.Add($"entries[{index}]: text is not valid Unicode, skipped."); continue; }
                        if (e == null) continue;
                        if (!ids.Add(e.Id)) { findings.Add($"entries[{index}]: duplicate id {e.Id} skipped."); continue; }
                        if (policy != null && (policy.LooksSensitive(e.Text) || policy.IsExcludedApp(e.SourceApp)))
                        {
                            findings.Add($"entries[{index}]: skipped because it matches the sensitive-data policy.");
                            continue;
                        }

                        entries.Add(e);
                    }
                }
            }
            catch (JsonException ex)
            {
                entries.Clear();
                findings.Add("History file is corrupt (" + ex.Message + "); starting empty.");
            }

            return new LoadedHistory(entries, findings);
        }

        private static ClipboardEntry ReadEntry(JsonElement item, int index, List<string> findings)
        {
            if (item.ValueKind != JsonValueKind.Object) { findings.Add($"entries[{index}]: not an object, skipped."); return null; }

            if (!item.TryGetProperty("text", out JsonElement t) || t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(t.GetString()))
            {
                findings.Add($"entries[{index}]: missing or empty 'text', skipped.");
                return null;
            }

            if (!item.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out long id) || id < 0)
            {
                findings.Add($"entries[{index}]: missing or invalid 'id', skipped.");
                return null;
            }

            string text = t.GetString();
            ClipKind kind = ClipKindDetector.Detect(text);
            if (item.TryGetProperty("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String &&
                Enum.TryParse(k.GetString(), true, out ClipKind parsed) && Enum.IsDefined(typeof(ClipKind), parsed))
                kind = parsed;

            DateTimeOffset created = DateTimeOffset.MinValue;
            if (item.TryGetProperty("createdAt", out JsonElement c) && c.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset when))
                created = when;
            else
                findings.Add($"entries[{index}]: bad 'createdAt', treated as very old.");

            bool pinned = item.TryGetProperty("pinned", out JsonElement p) && p.ValueKind == JsonValueKind.True;
            string app = item.TryGetProperty("sourceApp", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            return new ClipboardEntry(id, text, kind, created, pinned, app);
        }
    }

    /// <summary>Write-temp-then-replace so a crash mid-save never leaves a half-written history file.</summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes <paramref name="content"/> to "path.tmp" with <paramref name="writeAllText"/>, then calls
        /// <paramref name="replace"/>(temp, path). Returns null on success or an error message (the original file is untouched).
        /// </summary>
        public static string Write(string path, string content, Action<string, string> writeAllText, Action<string, string> replace, Action<string> deleteQuietly = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
            if (content == null) throw new ArgumentNullException(nameof(content));
            string temp = path + ".tmp";
            try
            {
                writeAllText(temp, content);
                replace(temp, path);
                return null;
            }
            catch (Exception ex)
            {
                try { deleteQuietly?.Invoke(temp); } catch (Exception) { /* best effort */ }
                return "Could not save clipboard history: " + ex.Message;
            }
        }

        /// <summary>Write using the real file system (creates the directory, UTF-8 without BOM, File.Replace when the target exists).</summary>
        public static string WriteToDisk(string path, string content)
        {
            return Write(path, content,
                (p, c) =>
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(p, c, new UTF8Encoding(false));
                },
                (tmp, dest) =>
                {
                    if (File.Exists(dest)) File.Replace(tmp, dest, null);
                    else File.Move(tmp, dest);
                },
                p => { if (File.Exists(p)) File.Delete(p); });
        }
    }
}
