using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CairoDesktop.Features.ClipboardHistory
{
    /// <summary>What a clip looks like. Used for icons and filtering only; it never changes the stored text.</summary>
    public enum ClipKind
    {
        Plain = 0,
        Url,
        FilePath,
        Color,
        Email,
        Number,
        Json,
        Code
    }

    /// <summary>Heuristic classification of clipboard text. Pure; never throws.</summary>
    public static class ClipKindDetector
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        private static readonly Regex ColorRegex = new Regex(
            @"^(#([0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})|rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(,\s*(0|1|0?\.\d+|\d{1,3}%)\s*)?\))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

        private static readonly Regex EmailRegex = new Regex(
            @"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$",
            RegexOptions.CultureInvariant, RegexTimeout);

        private static readonly Regex NumberRegex = new Regex(
            @"^[+\-]?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?([eE][+\-]?\d+)?$",
            RegexOptions.CultureInvariant, RegexTimeout);

        private static readonly Regex WinPathRegex = new Regex(
            @"^([A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/])[^<>|""?*\r\n]*$",
            RegexOptions.CultureInvariant, RegexTimeout);

        private static readonly Regex UnixPathRegex = new Regex(
            @"^(~|/)?(/[\w.\-]+){2,}/?$",
            RegexOptions.CultureInvariant, RegexTimeout);

        /// <summary>Classifies <paramref name="text"/>. Null/blank is <see cref="ClipKind.Plain"/>.</summary>
        public static ClipKind Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ClipKind.Plain;
            string t = text.Trim();

            try
            {
                bool multiLine = t.IndexOf('\n') >= 0 || t.IndexOf('\r') >= 0;

                if (!multiLine)
                {
                    if (IsUrl(t)) return ClipKind.Url;
                    if (ColorRegex.IsMatch(t)) return ClipKind.Color;
                    if (EmailRegex.IsMatch(t)) return ClipKind.Email;
                    if (NumberRegex.IsMatch(t)) return ClipKind.Number;
                    if (WinPathRegex.IsMatch(t) || UnixPathRegex.IsMatch(t)) return ClipKind.FilePath;
                }

                if ((t[0] == '{' && t[t.Length - 1] == '}') || (t[0] == '[' && t[t.Length - 1] == ']'))
                {
                    if (IsJson(t)) return ClipKind.Json;
                }

                if (LooksLikeCode(t)) return ClipKind.Code;
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological input: fall through to Plain rather than stalling the clipboard thread.
            }

            return ClipKind.Plain;
        }

        private static bool IsUrl(string t)
        {
            if (t.IndexOfAny(new[] { ' ', '\t' }) >= 0) return false;
            if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && t.Length > 6 && t.IndexOf('.', 4) > 0) return true;
            if (!Uri.TryCreate(t, UriKind.Absolute, out Uri uri)) return false;
            string s = uri.Scheme;
            return s == Uri.UriSchemeHttp || s == Uri.UriSchemeHttps || s == Uri.UriSchemeFtp || s == "ftps";
        }

        private static bool IsJson(string t)
        {
            try
            {
                using (JsonDocument.Parse(t)) return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool LooksLikeCode(string t)
        {
            string[] lines = t.Split('\n');
            if (lines.Length < 2) return false;

            int braces = 0, semis = 0;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line[line.Length - 1] == ';') semis++;
                if (line[line.Length - 1] == '{' || line.TrimStart().StartsWith("}", StringComparison.Ordinal)) braces++;
            }

            bool hasPair = t.IndexOf('{') >= 0 && t.IndexOf('}') >= 0;
            return (hasPair && (braces >= 2 || semis >= 1)) || semis >= 2;
        }

        /// <summary>Parses a numeric clip using invariant culture (thousands commas allowed). False if not a plain number.</summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            return double.TryParse(text.Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
