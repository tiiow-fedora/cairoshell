using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CairoDesktop.Features.ClipboardHistory
{
    /// <summary>
    /// The clipboard markers password managers set, read by the adapter and passed in as plain data.
    /// <c>ExcludeClipboardContentFromMonitorProcessing</c> present; <c>CanIncludeInClipboardHistory</c> present and 0;
    /// <c>CanUploadToCloudClipboard</c> present and 0.
    /// </summary>
    public readonly struct ClipboardFormatFlags
    {
        public ClipboardFormatFlags(bool excludeFromMonitorProcessing, bool historyDisallowed, bool cloudUploadDisallowed)
        {
            ExcludeFromMonitorProcessing = excludeFromMonitorProcessing;
            HistoryDisallowed = historyDisallowed;
            CloudUploadDisallowed = cloudUploadDisallowed;
        }

        public bool ExcludeFromMonitorProcessing { get; }
        public bool HistoryDisallowed { get; }
        public bool CloudUploadDisallowed { get; }

        public static ClipboardFormatFlags None => default(ClipboardFormatFlags);
    }

    public enum SensitiveReason
    {
        None = 0,
        ExcludedApp,
        FormatFlag,
        CreditCard,
        AwsAccessKey,
        PrivateKey,
        Jwt,
        HighEntropyToken
    }

    public readonly struct SensitiveDecision
    {
        public SensitiveDecision(SensitiveReason reason) { Reason = reason; }
        public SensitiveReason Reason { get; }
        /// <summary>True when the clip may be recorded.</summary>
        public bool Allowed => Reason == SensitiveReason.None;
    }

    public sealed class SensitivePolicyOptions
    {
        /// <summary>Process-name globs (<c>*</c> and <c>?</c>), case-insensitive, matched with and without a trailing ".exe".</summary>
        public IList<string> ExcludedApps { get; set; } = new List<string>();

        public bool HonorExcludeFromMonitorFlag { get; set; } = true;
        public bool HonorHistoryDisallowedFlag { get; set; } = true;
        /// <summary>Some managers only set the cloud flag; treating it as "private" is the safer reading.</summary>
        public bool HonorCloudUploadDisallowedFlag { get; set; } = true;

        public bool DetectCreditCards { get; set; } = true;
        public bool DetectAwsKeys { get; set; } = true;
        public bool DetectPrivateKeys { get; set; } = true;
        public bool DetectJwt { get; set; } = true;
        public bool DetectHighEntropyTokens { get; set; } = true;

        /// <summary>Single-token clips at least this long are candidates for the entropy heuristic.</summary>
        public int MinTokenLength { get; set; } = 24;

        /// <summary>Shannon entropy (bits per char) a candidate token must reach.</summary>
        public double MinTokenEntropy { get; set; } = 4.0;
    }

    /// <summary>
    /// Decides whether a clip may be recorded at all. A clip that fails is never stored, never shown and never persisted.
    /// <para>
    /// Decisions on ambiguous cases (all covered by tests): URLs, file paths, e-mail addresses, JSON, colours and
    /// numbers are never treated as tokens, so long links stay recordable. Git object ids (pure hex, 40 or 64 chars) and
    /// other pure-hex strings are allowed because hex has only 16 symbols and cannot reach the entropy bar; this means a
    /// hex-encoded API key is NOT caught (accepted trade-off against hiding every checksum). Sentences are never tokens
    /// since they contain whitespace. A 13-digit run starting with 1 is read as an epoch-milliseconds timestamp, not a card.
    /// </para>
    /// </summary>
    public sealed class SensitivePolicy
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        private static readonly Regex CardCandidate = new Regex(@"(?<!\d)(?:\d[\p{Zs}\-]?){12,18}\d(?!\d)", RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex AwsKey = new Regex(@"(?<![A-Za-z0-9])(AKIA|ASIA)[0-9A-Z]{16}(?![A-Za-z0-9])", RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex PrivateKey = new Regex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY( BLOCK)?-----", RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex Jwt = new Regex(@"(?<![A-Za-z0-9_\-])eyJ[A-Za-z0-9_\-]{5,}\.eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*", RegexOptions.CultureInvariant, RegexTimeout);
        private static readonly Regex HexOnly = new Regex(@"^[0-9a-fA-F]+$", RegexOptions.CultureInvariant, RegexTimeout);

        private readonly SensitivePolicyOptions _o;
        private readonly Regex[] _appGlobs;

        public SensitivePolicy(SensitivePolicyOptions options = null)
        {
            _o = options ?? new SensitivePolicyOptions();
            _appGlobs = (_o.ExcludedApps ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => GlobToRegex(g.Trim()))
                .ToArray();
        }

        /// <summary>Evaluates a clip. Never throws; a regex timeout is treated as "not detected" for that rule.</summary>
        public SensitiveDecision Evaluate(string text, string sourceApp = null, ClipboardFormatFlags flags = default(ClipboardFormatFlags))
        {
            if ((_o.HonorExcludeFromMonitorFlag && flags.ExcludeFromMonitorProcessing) ||
                (_o.HonorHistoryDisallowedFlag && flags.HistoryDisallowed) ||
                (_o.HonorCloudUploadDisallowedFlag && flags.CloudUploadDisallowed))
                return new SensitiveDecision(SensitiveReason.FormatFlag);

            if (IsExcludedApp(sourceApp)) return new SensitiveDecision(SensitiveReason.ExcludedApp);

            if (string.IsNullOrEmpty(text)) return new SensitiveDecision(SensitiveReason.None);
            return new SensitiveDecision(ScanText(text));
        }

        /// <summary>Text-only check, used when loading persisted files as a second line of defence.</summary>
        public bool LooksSensitive(string text) => !string.IsNullOrEmpty(text) && ScanText(text) != SensitiveReason.None;

        public bool IsExcludedApp(string sourceApp)
        {
            if (string.IsNullOrWhiteSpace(sourceApp) || _appGlobs.Length == 0) return false;
            string name = sourceApp.Trim();
            string bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
            foreach (Regex g in _appGlobs)
                if (g.IsMatch(name) || g.IsMatch(bare)) return true;
            return false;
        }

        private SensitiveReason ScanText(string text)
        {
            try
            {
                if (_o.DetectPrivateKeys && PrivateKey.IsMatch(text)) return SensitiveReason.PrivateKey;
                if (_o.DetectAwsKeys && AwsKey.IsMatch(text)) return SensitiveReason.AwsAccessKey;
                if (_o.DetectJwt && Jwt.IsMatch(text)) return SensitiveReason.Jwt;
                if (_o.DetectCreditCards && ContainsCardNumber(text)) return SensitiveReason.CreditCard;
                if (_o.DetectHighEntropyTokens && IsSecretLikeToken(text)) return SensitiveReason.HighEntropyToken;
            }
            catch (RegexMatchTimeoutException)
            {
                // Unknown = allowed; the alternative (dropping the clip) would make odd text uncopyable-to-history silently.
            }

            return SensitiveReason.None;
        }

        private static bool ContainsCardNumber(string text)
        {
            string whole = text.Trim();
            foreach (Match m in CardCandidate.Matches(text))
            {
                // Bare digit runs only count when the clip is just the number; otherwise ids and timestamps inside prose would trip.
                if (m.Value.All(char.IsDigit))
                {
                    if (m.Value == whole && IsCardDigits(m.Value)) return true;
                    continue;
                }

                // The greedy match may swallow digits that follow the card ("4111 1111 1111 1111 12/25"), so also try
                // every run of whole separator-delimited groups inside it.
                string[] groups = m.Value.Split(new[] { '-' }.Concat(m.Value.Where(char.IsSeparator)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
                for (int start = 0; start < groups.Length; start++)
                {
                    var sb = new StringBuilder();
                    for (int end = start; end < groups.Length; end++)
                    {
                        sb.Append(groups[end]);
                        if (sb.Length > 19) break;
                        if (end > start && IsCardDigits(sb.ToString())) return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCardDigits(string digits)
        {
            if (digits.Length < 13 || digits.Length > 19) return false;
            if (digits.Distinct().Count() == 1) return false;
            if (digits.Length == 13 && digits[0] == '1') return false; // epoch milliseconds
            return IsLuhnValid(digits);
        }

        /// <summary>Luhn checksum over a string of ASCII digits. Non-digits make it false.</summary>
        public static bool IsLuhnValid(string digits)
        {
            if (string.IsNullOrEmpty(digits)) return false;
            int sum = 0;
            bool dbl = false;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                char c = digits[i];
                if (c < '0' || c > '9') return false;
                int d = c - '0';
                if (dbl)
                {
                    d *= 2;
                    if (d > 9) d -= 9;
                }

                sum += d;
                dbl = !dbl;
            }

            return sum % 10 == 0;
        }

        private bool IsSecretLikeToken(string text)
        {
            string t = text.Trim();
            if (t.Length < _o.MinTokenLength) return false;
            foreach (char c in t) if (char.IsWhiteSpace(c)) return false;
            if (HexOnly.IsMatch(t)) return false;

            // Structured kinds are recognisable and not secrets in themselves.
            ClipKind kind = ClipKindDetector.Detect(t);
            if (kind != ClipKind.Plain) return false;
            if (t.Contains("://")) return false;

            bool lower = false, upper = false, digit = false, symbol = false;
            foreach (char c in t)
            {
                if (char.IsLower(c)) lower = true;
                else if (char.IsUpper(c)) upper = true;
                else if (char.IsDigit(c)) digit = true;
                else symbol = true;
            }

            int classes = (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0);
            if (classes < 3) return false;
            return ShannonEntropy(t) >= _o.MinTokenEntropy;
        }

        /// <summary>Shannon entropy in bits per UTF-16 char.</summary>
        public static double ShannonEntropy(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var counts = new Dictionary<char, int>();
            foreach (char c in s)
            {
                counts.TryGetValue(c, out int n);
                counts[c] = n + 1;
            }

            double h = 0;
            foreach (int n in counts.Values)
            {
                double p = (double)n / s.Length;
                h -= p * Math.Log(p, 2);
            }

            return h;
        }

        internal static Regex GlobToRegex(string glob)
        {
            var sb = new StringBuilder("^");
            foreach (char c in glob)
            {
                if (c == '*') sb.Append(".*");
                else if (c == '?') sb.Append('.');
                else sb.Append(Regex.Escape(c.ToString()));
            }

            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
    }
}
