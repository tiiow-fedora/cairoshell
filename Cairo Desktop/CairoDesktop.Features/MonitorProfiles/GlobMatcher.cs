namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>Case-insensitive glob: <c>*</c> matches any run of characters (including none), <c>?</c> exactly one.</summary>
    public static class GlobMatcher
    {
        public static bool HasWildcard(string pattern) => pattern != null && pattern.IndexOfAny(new[] { '*', '?' }) >= 0;

        /// <summary>Number of characters in the pattern that are not wildcards.</summary>
        public static int LiteralCount(string pattern)
        {
            int n = 0;
            foreach (char c in pattern ?? "") if (c != '*' && c != '?') n++;
            return n;
        }

        public static bool IsMatch(string pattern, string text)
        {
            if (pattern == null || text == null) return false;
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], text[t])))
                {
                    p++;
                    t++;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static bool Same(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }
}
