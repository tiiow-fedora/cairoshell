using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>
    /// Fans a query out to providers, filters and ranks the items and returns the top few.
    ///
    /// Ranking: for a non-empty text, the match score is the best of the fuzzy score on the title, 60% of the score on
    /// a keyword and 40% of the score on the subtitle; items with no match are dropped. The final score is
    /// <c>match * (1 + boost)</c> where boost is 0.5 * log2(1 + frecency) capped at 1.0 (so at most double), so frecency reorders
    /// comparable matches but cannot resurrect a poor one. With empty text every item has the same base score, so the
    /// list is simply "most used first, then alphabetical". Items with a <see cref="PaletteItem.PinnedScore"/> skip
    /// matching and frecency. Ties break on frecency, then title (ordinal, ignoring case), then id, then provider order.
    /// Items whose id was already returned by an earlier provider are dropped.
    ///
    /// Thread-safety: single-threaded by contract. Call Search/RecordLaunch from one thread (the UI thread);
    /// the frecency store itself is thread-safe.
    /// </summary>
    public sealed class PaletteEngine
    {
        private const int MaxItemsPerProvider = 5000;
        private const double TitleWeight = 1.0, KeywordWeight = 0.6, SubtitleWeight = 0.4;

        private readonly IReadOnlyList<ICommandProvider> _providers;
        private readonly FrecencyStore _frecency;

        public PaletteEngine(IEnumerable<ICommandProvider> providers, FrecencyStore frecency = null)
        {
            var list = new List<ICommandProvider>();
            if (providers != null)
            {
                foreach (var p in providers)
                {
                    if (p != null) list.Add(p);
                }
            }
            _providers = list;
            _frecency = frecency;
        }

        /// <summary>Builds the engine for a config: user commands, calculator and web search as enabled, plus any host providers (e.g. apps).</summary>
        public static PaletteEngine Create(PaletteConfig config, IEnumerable<ICommandProvider> hostProviders, FrecencyStore frecency)
        {
            config = config ?? new PaletteConfig();
            var providers = new List<ICommandProvider>();
            if (config.Providers.Commands) providers.Add(new UserCommandProvider(config.Commands));
            if (hostProviders != null) providers.AddRange(hostProviders);
            if (config.Providers.Calculator) providers.Add(new CalculatorProvider());
            if (config.Providers.WebSearch) providers.Add(new WebSearchProvider(config.WebSearchUrl));
            return new PaletteEngine(providers, frecency);
        }

        public FrecencyStore Frecency => _frecency;

        public PaletteSearchResult Search(string rawQuery, int maxResults)
        {
            PaletteQuery query = PaletteQuery.Parse(rawQuery);
            var diagnostics = new List<string>();
            var candidates = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string category = PaletteQuery.CategoryFor(query.Mode);
            bool hasText = query.Text.Length > 0;

            for (int pIndex = 0; pIndex < _providers.Count; pIndex++)
            {
                ICommandProvider provider = _providers[pIndex];
                string name = SafeName(provider);
                IReadOnlyList<PaletteItem> items;
                try
                {
                    items = provider.Query(query);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Provider '{name}' failed: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (items == null) continue;
                int count = Math.Min(items.Count, MaxItemsPerProvider);
                if (items.Count > MaxItemsPerProvider) diagnostics.Add($"Provider '{name}' returned {items.Count} items; only the first {MaxItemsPerProvider} were used.");

                for (int i = 0; i < count; i++)
                {
                    PaletteItem item = items[i];
                    if (item == null) continue;
                    if (category != null && !string.Equals(item.Category, category, StringComparison.Ordinal)) continue;
                    if (!seen.Add(item.Id)) continue;

                    Candidate c = Score(item, query, hasText, name, pIndex);
                    if (c != null) candidates.Add(c);
                }
            }

            candidates.Sort(Compare);

            int limit = Math.Max(0, maxResults);
            var results = new List<PaletteResult>(Math.Min(limit, candidates.Count));
            for (int i = 0; i < candidates.Count && i < limit; i++)
            {
                Candidate c = candidates[i];
                results.Add(new PaletteResult(c.Item, c.Score, c.Indices, c.Provider));
            }

            return new PaletteSearchResult(query, results, diagnostics);
        }

        /// <summary>Records that the user ran <paramref name="item"/>, so it ranks higher next time. Pinned items (calculator, web) are not recorded.</summary>
        public void RecordLaunch(PaletteItem item)
        {
            if (item == null || item.PinnedScore.HasValue || _frecency == null) return;
            _frecency.Record(item.Id);
        }

        private Candidate Score(PaletteItem item, PaletteQuery query, bool hasText, string provider, int providerIndex)
        {
            if (item.PinnedScore.HasValue)
            {
                return new Candidate(item, item.PinnedScore.Value, null, 0, provider, providerIndex);
            }

            double match;
            int[] indices = null;

            if (!hasText)
            {
                match = 100;
            }
            else
            {
                FuzzyMatch title = FuzzyMatcher.Match(query.Text, item.Title);
                match = title.IsMatch ? title.Score * TitleWeight : 0;
                if (title.IsMatch) indices = title.Indices;

                foreach (string keyword in item.Keywords)
                {
                    FuzzyMatch k = FuzzyMatcher.Match(query.Text, keyword);
                    if (k.IsMatch && k.Score * KeywordWeight > match)
                    {
                        match = k.Score * KeywordWeight;
                        indices = null;
                    }
                }

                if (item.Subtitle.Length > 0)
                {
                    FuzzyMatch s = FuzzyMatcher.Match(query.Text, item.Subtitle);
                    if (s.IsMatch && s.Score * SubtitleWeight > match)
                    {
                        match = s.Score * SubtitleWeight;
                        indices = null;
                    }
                }

                if (match <= 0) return null;
            }

            double frecency = _frecency == null ? 0 : _frecency.GetScore(item.Id);
            double boost = Math.Min(1.0, 0.5 * Math.Log(1 + frecency, 2));
            return new Candidate(item, match * (1 + boost), indices, frecency, provider, providerIndex);
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = b.Frecency.CompareTo(a.Frecency);
            if (c != 0) return c;
            c = StringComparer.OrdinalIgnoreCase.Compare(a.Item.Title, b.Item.Title);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Item.Id, b.Item.Id);
            if (c != 0) return c;
            return a.ProviderIndex.CompareTo(b.ProviderIndex);
        }

        private static string SafeName(ICommandProvider p)
        {
            try
            {
                string n = p.Name;
                return string.IsNullOrEmpty(n) ? p.GetType().Name : n;
            }
            catch (Exception)
            {
                return p.GetType().Name;
            }
        }

        private sealed class Candidate
        {
            public Candidate(PaletteItem item, double score, int[] indices, double frecency, string provider, int providerIndex)
            {
                Item = item;
                Score = score;
                Indices = indices;
                Frecency = frecency;
                Provider = provider;
                ProviderIndex = providerIndex;
            }

            public PaletteItem Item { get; }
            public double Score { get; }
            public int[] Indices { get; }
            public double Frecency { get; }
            public string Provider { get; }
            public int ProviderIndex { get; }
        }
    }
}
