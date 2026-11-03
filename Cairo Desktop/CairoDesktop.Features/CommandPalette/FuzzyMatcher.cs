using System;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>Outcome of <see cref="FuzzyMatcher.Match"/>.</summary>
    public readonly struct FuzzyMatch
    {
        public static readonly FuzzyMatch None = new FuzzyMatch(false, 0, Array.Empty<int>());

        public FuzzyMatch(bool isMatch, int score, int[] indices)
        {
            IsMatch = isMatch;
            Score = score;
            Indices = indices ?? Array.Empty<int>();
        }

        public bool IsMatch { get; }

        /// <summary>Higher is better. Always at least 1 for a match, 0 for no match.</summary>
        public int Score { get; }

        /// <summary>Ascending UTF-16 indices into the matched text, for highlighting. Never null.</summary>
        public int[] Indices { get; }
    }

    /// <summary>
    /// Case-insensitive subsequence matcher. It finds the best-scoring alignment with a small dynamic program
    /// (so "cd" prefers "CairoDesktop" word starts over scattered letters) and reports the matched indices.
    /// Bonuses: consecutive characters, word starts (after a non-alphanumeric or a camelCase hump), prefix,
    /// exact-case. Penalties: gaps between matches and a late first match.
    /// Matching is per UTF-16 code unit with <see cref="char.ToLowerInvariant"/>, so it is culture independent.
    /// Only the first <see cref="MaxTextLength"/> characters of the text and the first <see cref="MaxPatternLength"/>
    /// of the pattern are considered, which bounds the work for pathological input.
    /// Thread-safety: stateless and safe to call from any thread.
    /// </summary>
    public static class FuzzyMatcher
    {
        public const int MaxTextLength = 256;
        public const int MaxPatternLength = 64;

        private const int MatchBase = 16;
        private const int ConsecutiveBonus = 12;
        private const int BoundaryBonus = 10;
        private const int CamelBonus = 8;
        private const int DigitBonus = 3;
        private const int PrefixBonus = 10;
        private const int CaseBonus = 1;
        private const int GapStart = 3;
        private const int LeadingPenaltyMax = 12;
        private const int NoScore = int.MinValue / 2;

        /// <summary>Matches <paramref name="pattern"/> against <paramref name="text"/>. An empty pattern matches with score 1 and no indices.</summary>
        public static FuzzyMatch Match(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return new FuzzyMatch(true, 1, null);
            if (string.IsNullOrEmpty(text)) return FuzzyMatch.None;

            int m = Math.Min(pattern.Length, MaxPatternLength);
            int n = Math.Min(text.Length, MaxTextLength);
            if (m > n) return FuzzyMatch.None;

            // Cheap rejection: greedy subsequence test.
            int pi = 0;
            for (int j = 0; j < n && pi < m; j++)
            {
                if (char.ToLowerInvariant(text[j]) == char.ToLowerInvariant(pattern[pi])) pi++;
            }
            if (pi < m) return FuzzyMatch.None;

            var dp = new int[m * n];
            var from = new int[m * n];

            for (int i = 0; i < m; i++)
            {
                char pc = char.ToLowerInvariant(pattern[i]);
                int bestRun = NoScore;   // max over k <= j-2 of dp[i-1][k] + k
                int bestRunIdx = -1;

                for (int j = 0; j < n; j++)
                {
                    int cell = i * n + j;
                    dp[cell] = NoScore;
                    from[cell] = -1;

                    // Make k = j-2 available to the gap candidate before evaluating column j.
                    if (i > 0 && j >= 2)
                    {
                        int prev = dp[(i - 1) * n + (j - 2)];
                        if (prev > NoScore && prev + (j - 2) > bestRun)
                        {
                            bestRun = prev + (j - 2);
                            bestRunIdx = j - 2;
                        }
                    }

                    if (char.ToLowerInvariant(text[j]) != pc) continue;

                    // Never match half of a surrogate pair: a high half needs its low half to match next,
                    // and a low half may only follow the high half that matched just before it.
                    bool forceAdjacent = false;
                    if (char.IsHighSurrogate(text[j]))
                    {
                        if (i + 1 >= m || j + 1 >= n || pattern[i + 1] != text[j + 1]) continue;
                    }
                    else if (char.IsLowSurrogate(text[j]))
                    {
                        if (i == 0 || j == 0 || pattern[i - 1] != text[j - 1]) continue;
                        forceAdjacent = true;
                    }

                    int gain = MatchBase + PositionBonus(text, j) + (text[j] == pattern[i] ? CaseBonus : 0);

                    if (i == 0)
                    {
                        dp[cell] = gain - Math.Min(j, LeadingPenaltyMax) + (j == 0 ? PrefixBonus : 0);
                        continue;
                    }

                    int best = NoScore;
                    int bestFrom = -1;

                    if (j >= 1)
                    {
                        int adjacent = dp[(i - 1) * n + (j - 1)];
                        if (adjacent > NoScore)
                        {
                            best = adjacent + ConsecutiveBonus;
                            bestFrom = j - 1;
                        }
                    }

                    if (bestRunIdx >= 0 && !forceAdjacent)
                    {
                        // dp[k] - GapStart - (j - k - 1), written with the running max of dp[k] + k.
                        int viaGap = bestRun - j + 1 - GapStart;
                        if (viaGap > best)
                        {
                            best = viaGap;
                            bestFrom = bestRunIdx;
                        }
                    }

                    if (bestFrom < 0) continue;
                    dp[cell] = best + gain;
                    from[cell] = bestFrom;
                }
            }

            int endScore = NoScore;
            int endIdx = -1;
            for (int j = 0; j < n; j++)
            {
                int v = dp[(m - 1) * n + j];
                if (v > endScore) { endScore = v; endIdx = j; }
            }

            if (endIdx < 0) return FuzzyMatch.None;

            var indices = new int[m];
            int col = endIdx;
            for (int i = m - 1; i >= 0; i--)
            {
                indices[i] = col;
                col = from[i * n + col];
            }

            return new FuzzyMatch(true, Math.Max(1, endScore), indices);
        }

        private static int PositionBonus(string text, int j)
        {
            if (j == 0) return BoundaryBonus;
            char prev = text[j - 1];
            char cur = text[j];
            if (!char.IsLetterOrDigit(prev)) return BoundaryBonus;
            if (char.IsLower(prev) && char.IsUpper(cur)) return CamelBonus;
            if (char.IsLetter(prev) && char.IsDigit(cur)) return DigitBonus;
            return 0;
        }
    }
}
