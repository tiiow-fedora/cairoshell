using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CairoDesktop.Features.ClipboardHistory
{
    /// <summary>Outcome of a transform. Transforms never throw on bad input; they return a failure with a short message.</summary>
    public sealed class TransformResult
    {
        private TransformResult(bool ok, string output, string error) { Success = ok; Output = output; Error = error; }
        public bool Success { get; }
        public string Output { get; }
        public string Error { get; }
        public static TransformResult Ok(string output) => new TransformResult(true, output, null);
        public static TransformResult Fail(string error) => new TransformResult(false, null, error);
    }

    public interface ITransform
    {
        /// <summary>Lowercase, hyphenated, unique key such as "json-pretty".</summary>
        string Name { get; }
        string Description { get; }
        TransformResult Apply(string input);
    }

    /// <summary>Pure text transforms for the "paste as" menu. Output newlines are "\n" unless noted.</summary>
    public static class ClipTransforms
    {
        public static TransformResult Trim(string s) => Guard(s, x => x.Trim());

        public static TransformResult Upper(string s) => Guard(s, x => x.ToUpperInvariant());

        public static TransformResult Lower(string s) => Guard(s, x => x.ToLowerInvariant());

        /// <summary>Capitalises the first letter of each word and lowercases the rest. Surrogate pairs pass through untouched.</summary>
        public static TransformResult TitleCase(string s)
        {
            return Guard(s, x =>
            {
                var sb = new StringBuilder(x.Length);
                bool startOfWord = true;
                for (int i = 0; i < x.Length; i++)
                {
                    char c = x[i];
                    if (char.IsHighSurrogate(c) && i + 1 < x.Length && char.IsLowSurrogate(x[i + 1]))
                    {
                        sb.Append(c).Append(x[i + 1]);
                        i++;
                        startOfWord = false; // an astral letter or emoji counts as part of a word
                        continue;
                    }

                    if (char.IsLetter(c))
                    {
                        sb.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                        startOfWord = false;
                    }
                    else
                    {
                        sb.Append(c);
                        if (char.IsDigit(c) || c == '\'' || c == '’') startOfWord = false;
                        else startOfWord = true;
                    }
                }

                return sb.ToString();
            });
        }

        /// <summary>
        /// Plain text clean-up: LF newlines, no zero-width characters, no ANSI escapes, exotic spaces become a normal
        /// space, trailing spaces removed from every line.
        /// </summary>
        public static TransformResult StripFormatting(string s)
        {
            return Guard(s, x =>
            {
                x = AnsiRegex.Replace(x, "");
                var sb = new StringBuilder(x.Length);
                foreach (char c in x)
                {
                    if (c == '​' || c == '‌' || c == '‍' || c == '﻿' || c == '⁠' || c == '­') continue;
                    if (c == ' ' || c == ' ' || c == ' ' || (c >= ' ' && c <= ' ') || c == '　') sb.Append(' ');
                    else sb.Append(c);
                }

                string[] lines = SplitLines(sb.ToString());
                return string.Join("\n", lines.Select(l => l.TrimEnd(' ', '\t')));
            });
        }

        /// <summary>Joins all non-empty lines (each trimmed) with a single space.</summary>
        public static TransformResult JoinLines(string s)
        {
            return Guard(s, x => string.Join(" ", SplitLines(x).Select(l => l.Trim()).Where(l => l.Length > 0)));
        }

        /// <summary>Sorts lines case-insensitively (ordinal), ties broken by exact ordinal so the result is deterministic.</summary>
        public static TransformResult SortLines(string s)
        {
            return Guard(s, x =>
            {
                string nl = DetectNewline(x);
                List<string> lines = SplitLines(x).ToList();
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1); // trailing newline
                return string.Join(nl, lines
                    .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l, StringComparer.Ordinal));
            });
        }

        /// <summary>Removes repeated lines, keeping the first occurrence (exact, case-sensitive match).</summary>
        public static TransformResult DedupeLines(string s)
        {
            return Guard(s, x =>
            {
                string nl = DetectNewline(x);
                List<string> lines = SplitLines(x).ToList();
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                return string.Join(nl, lines.Where(seen.Add));
            });
        }

        /// <summary>Percent-encodes everything except unreserved characters (space becomes %20, not +).</summary>
        public static TransformResult UrlEncode(string s)
        {
            if (s == null) return Fail("No text.");
            try
            {
                var sb = new StringBuilder();
                // Chunked: Uri.EscapeDataString has a ~32k limit on older runtimes.
                const int chunk = 8000;
                int i = 0;
                while (i < s.Length)
                {
                    int len = Math.Min(chunk, s.Length - i);
                    if (i + len < s.Length && char.IsHighSurrogate(s[i + len - 1])) len++; // keep pairs together
                    sb.Append(Uri.EscapeDataString(s.Substring(i, len)));
                    i += len;
                }

                return TransformResult.Ok(sb.ToString());
            }
            catch (UriFormatException)
            {
                return Fail("Text contains characters that cannot be URL-encoded.");
            }
        }

        /// <summary>Decodes %XX sequences. Malformed sequences stay as they are. "+" is not turned into a space.</summary>
        public static TransformResult UrlDecode(string s)
        {
            if (s == null) return Fail("No text.");
            try
            {
                return TransformResult.Ok(Uri.UnescapeDataString(s));
            }
            catch (UriFormatException)
            {
                return Fail("Not valid URL-encoded text.");
            }
        }

        public static TransformResult Base64Encode(string s)
        {
            if (s == null) return Fail("No text.");
            try
            {
                return TransformResult.Ok(Convert.ToBase64String(StrictUtf8.GetBytes(s)));
            }
            catch (EncoderFallbackException)
            {
                return Fail("Text contains an unpaired surrogate and cannot be encoded as UTF-8.");
            }
        }

        /// <summary>Decodes standard or URL-safe Base64 (padding optional, whitespace ignored) into UTF-8 text.</summary>
        public static TransformResult Base64Decode(string s)
        {
            if (s == null) return Fail("No text.");
            string t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
            if (t.Length == 0) return Fail("Nothing to decode.");
            t = t.TrimEnd('=');
            if (t.Length % 4 == 1) return Fail("Not valid Base64.");
            t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
            try
            {
                byte[] bytes = Convert.FromBase64String(t);
                return TransformResult.Ok(StrictUtf8.GetString(bytes));
            }
            catch (FormatException)
            {
                return Fail("Not valid Base64.");
            }
            catch (DecoderFallbackException)
            {
                return Fail("Decoded bytes are not UTF-8 text.");
            }
        }

        /// <summary>Re-indents JSON with two spaces. Unicode is kept as typed (no \uXXXX escaping).</summary>
        public static TransformResult JsonPretty(string s) => JsonRewrite(s, true);

        public static TransformResult JsonMinify(string s) => JsonRewrite(s, false);

        private static TransformResult JsonRewrite(string s, bool indented)
        {
            if (string.IsNullOrWhiteSpace(s)) return Fail("No JSON to format.");
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(s))
                using (var ms = new MemoryStream())
                {
                    var options = new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                    using (var writer = new Utf8JsonWriter(ms, options))
                        doc.WriteTo(writer);
                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    return TransformResult.Ok(UnescapeSurrogatePairs(text.Replace("\r\n", "\n")));
                }
            }
            catch (JsonException ex)
            {
                return Fail("Invalid JSON: " + ex.Message);
            }
        }

        /// <summary>
        /// System.Text.Json never writes characters outside the BMP raw, even with a relaxed encoder, so emoji come out as
        /// "\uD83D\uDE00". Put them back so reformatting does not mangle the clip. An escaped backslash is skipped as a pair
        /// so "\\uD83D" (a literal backslash followed by text) is left alone.
        /// </summary>
        private static string UnescapeSurrogatePairs(string s)
        {
            if (s.IndexOf("\\u", StringComparison.Ordinal) < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    if (s[i + 1] == 'u' && i + 11 < s.Length && s[i + 6] == '\\' && s[i + 7] == 'u'
                        && TryHex(s, i + 2, out int hi) && TryHex(s, i + 8, out int lo)
                        && hi >= 0xD800 && hi <= 0xDBFF && lo >= 0xDC00 && lo <= 0xDFFF)
                    {
                        sb.Append((char)hi).Append((char)lo);
                        i += 11;
                        continue;
                    }

                    sb.Append(c).Append(s[i + 1]);
                    i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static bool TryHex(string s, int start, out int value)
        {
            value = 0;
            if (start + 4 > s.Length) return false;
            return int.TryParse(s.Substring(start, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        // ---- shared helpers ----

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly Regex AnsiRegex = new Regex("\u001B\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

        private static TransformResult Fail(string message) => TransformResult.Fail(message);

        private static TransformResult Guard(string s, Func<string, string> f)
        {
            if (s == null) return Fail("No text.");
            return TransformResult.Ok(f(s));
        }

        internal static string[] SplitLines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static string DetectNewline(string s) => s.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>Named lookup of all transforms, in menu order.</summary>
    public sealed class TransformRegistry
    {
        private readonly List<ITransform> _all = new List<ITransform>();
        private readonly Dictionary<string, ITransform> _byName = new Dictionary<string, ITransform>(StringComparer.OrdinalIgnoreCase);

        private sealed class Delegated : ITransform
        {
            private readonly Func<string, TransformResult> _f;
            public Delegated(string name, string description, Func<string, TransformResult> f) { Name = name; Description = description; _f = f; }
            public string Name { get; }
            public string Description { get; }
            public TransformResult Apply(string input) => _f(input);
        }

        public static TransformRegistry CreateDefault()
        {
            var r = new TransformRegistry();
            r.Register("trim", "Trim leading and trailing whitespace", ClipTransforms.Trim);
            r.Register("upper", "UPPER CASE", ClipTransforms.Upper);
            r.Register("lower", "lower case", ClipTransforms.Lower);
            r.Register("title", "Title Case", ClipTransforms.TitleCase);
            r.Register("strip-formatting", "Plain text: LF newlines, no zero-width or ANSI codes", ClipTransforms.StripFormatting);
            r.Register("join-lines", "Join lines with spaces", ClipTransforms.JoinLines);
            r.Register("sort-lines", "Sort lines", ClipTransforms.SortLines);
            r.Register("dedupe-lines", "Remove duplicate lines", ClipTransforms.DedupeLines);
            r.Register("url-encode", "URL-encode", ClipTransforms.UrlEncode);
            r.Register("url-decode", "URL-decode", ClipTransforms.UrlDecode);
            r.Register("base64-encode", "Base64 encode", ClipTransforms.Base64Encode);
            r.Register("base64-decode", "Base64 decode", ClipTransforms.Base64Decode);
            r.Register("json-pretty", "Format JSON", ClipTransforms.JsonPretty);
            r.Register("json-minify", "Minify JSON", ClipTransforms.JsonMinify);
            return r;
        }

        public IReadOnlyList<ITransform> All => _all;

        public IEnumerable<string> Names => _all.Select(t => t.Name);

        public void Register(string name, string description, Func<string, TransformResult> f) => Register(new Delegated(name, description, f));

        /// <summary>Adds or replaces (by case-insensitive name) a transform.</summary>
        public void Register(ITransform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (string.IsNullOrWhiteSpace(transform.Name)) throw new ArgumentException("Transform needs a name.", nameof(transform));
            if (_byName.TryGetValue(transform.Name, out ITransform old)) _all.Remove(old);
            _byName[transform.Name] = transform;
            _all.Add(transform);
        }

        public bool TryGet(string name, out ITransform transform)
        {
            transform = null;
            return name != null && _byName.TryGetValue(name.Trim(), out transform);
        }

        /// <summary>Runs a transform by name; an unknown name is a failed result, not an exception.</summary>
        public TransformResult Apply(string name, string input)
        {
            if (!TryGet(name, out ITransform t)) return TransformResult.Fail("Unknown transform '" + name + "'.");
            try { return t.Apply(input); }
            catch (Exception ex) { return TransformResult.Fail("Transform '" + name + "' failed: " + ex.Message); }
        }
    }
}
