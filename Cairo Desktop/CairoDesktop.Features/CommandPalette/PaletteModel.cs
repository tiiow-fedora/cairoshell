using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>Well-known <see cref="PaletteItem.Category"/> values. Query modes filter on these.</summary>
    public static class PaletteCategories
    {
        public const string Command = "Command";
        public const string App = "App";
        public const string Calculator = "Calculator";
        public const string Web = "Web";
    }

    /// <summary>What the prefix of the query asked for.</summary>
    public enum PaletteMode
    {
        /// <summary>No prefix: every provider, every category.</summary>
        All,
        /// <summary><c>&gt;</c> commands only.</summary>
        Commands,
        /// <summary><c>?</c> web search.</summary>
        Web,
        /// <summary><c>=</c> calculator.</summary>
        Calculator,
        /// <summary><c>@</c> applications only.</summary>
        Apps
    }

    /// <summary>
    /// A parsed query.
    /// Thread-safety: immutable, safe to share.
    /// </summary>
    public sealed class PaletteQuery
    {
        /// <summary>Longest search text kept; anything beyond is cut to keep providers and URL building bounded.</summary>
        public const int MaxTextLength = 2000;

        public PaletteQuery(PaletteMode mode, string text, string raw)
        {
            Mode = mode;
            Text = text ?? "";
            Raw = raw ?? "";
        }

        public PaletteMode Mode { get; }

        /// <summary>Search text with the mode prefix removed and surrounding whitespace trimmed.</summary>
        public string Text { get; }

        /// <summary>The input as typed.</summary>
        public string Raw { get; }

        public static PaletteQuery Parse(string raw)
        {
            raw = raw ?? "";
            string s = raw.TrimStart();
            PaletteMode mode = PaletteMode.All;

            if (s.Length > 0)
            {
                switch (s[0])
                {
                    case '>': mode = PaletteMode.Commands; break;
                    case '?': mode = PaletteMode.Web; break;
                    case '=': mode = PaletteMode.Calculator; break;
                    case '@': mode = PaletteMode.Apps; break;
                }

                if (mode != PaletteMode.All) s = s.Substring(1);
            }

            s = s.Trim();
            if (s.Length > MaxTextLength) s = s.Substring(0, MaxTextLength);
            return new PaletteQuery(mode, s, raw);
        }

        /// <summary>The category a mode is restricted to, or null for <see cref="PaletteMode.All"/>.</summary>
        public static string CategoryFor(PaletteMode mode)
        {
            switch (mode)
            {
                case PaletteMode.Commands: return PaletteCategories.Command;
                case PaletteMode.Apps: return PaletteCategories.App;
                case PaletteMode.Calculator: return PaletteCategories.Calculator;
                case PaletteMode.Web: return PaletteCategories.Web;
                default: return null;
            }
        }
    }

    /// <summary>
    /// One entry in the palette.
    /// <see cref="Payload"/> is opaque to the engine: the host decides what it means (see <see cref="PaletteAction"/>
    /// for the convention the built-in providers use). An empty payload means "not actionable" (e.g. a calculator error).
    /// Thread-safety: immutable (Keywords is copied), safe to share.
    /// </summary>
    public sealed class PaletteItem
    {
        public PaletteItem(string id, string title, string subtitle = null, string category = null,
            IEnumerable<string> keywords = null, string payload = null, int? pinnedScore = null)
        {
            Id = id ?? "";
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Category = category ?? "";
            Payload = payload ?? "";
            PinnedScore = pinnedScore;
            var list = new List<string>();
            if (keywords != null)
            {
                foreach (var k in keywords)
                {
                    if (!string.IsNullOrWhiteSpace(k)) list.Add(k);
                }
            }
            Keywords = list.AsReadOnly();
        }

        /// <summary>Stable identity, used for de-duplication and frecency. Should be unique per provider.</summary>
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string Category { get; }
        public IReadOnlyList<string> Keywords { get; }
        public string Payload { get; }

        /// <summary>
        /// When set, the item is not fuzzy-matched: it is shown with this score regardless of the text
        /// (used for calculator and web-search answers) and does not collect frecency.
        /// </summary>
        public int? PinnedScore { get; }
    }

    /// <summary>
    /// A source of palette items. Providers should be cheap and side-effect free; the engine calls them synchronously
    /// and treats any exception as a diagnostic. A provider is told the whole query and may return everything it has
    /// (the engine does the fuzzy filtering) or pre-filter for efficiency.
    /// Thread-safety: the engine calls providers from one thread at a time.
    /// </summary>
    public interface ICommandProvider
    {
        string Name { get; }
        IReadOnlyList<PaletteItem> Query(PaletteQuery query);
    }

    /// <summary>A provider over a fixed list of items. Useful for apps and for tests. Thread-safety: immutable.</summary>
    public sealed class StaticItemProvider : ICommandProvider
    {
        private readonly PaletteItem[] _items;

        public StaticItemProvider(string name, IEnumerable<PaletteItem> items)
        {
            Name = name ?? "static";
            _items = items == null ? Array.Empty<PaletteItem>() : new List<PaletteItem>(items).ToArray();
        }

        public string Name { get; }

        public IReadOnlyList<PaletteItem> Query(PaletteQuery query) => _items;
    }

    /// <summary>An item the engine decided to show. Thread-safety: immutable.</summary>
    public sealed class PaletteResult
    {
        public PaletteResult(PaletteItem item, double score, int[] titleMatchIndices, string provider)
        {
            Item = item;
            Score = score;
            TitleMatchIndices = titleMatchIndices ?? Array.Empty<int>();
            Provider = provider ?? "";
        }

        public PaletteItem Item { get; }

        /// <summary>Final rank score (match score times frecency boost).</summary>
        public double Score { get; }

        /// <summary>Indices into <see cref="PaletteItem.Title"/> to highlight; empty when the match was via keywords or subtitle.</summary>
        public int[] TitleMatchIndices { get; }

        public string Provider { get; }
    }

    /// <summary>Everything one search produced. Thread-safety: immutable.</summary>
    public sealed class PaletteSearchResult
    {
        public PaletteSearchResult(PaletteQuery query, IReadOnlyList<PaletteResult> results, IReadOnlyList<string> diagnostics)
        {
            Query = query;
            Results = results;
            Diagnostics = diagnostics;
        }

        public PaletteQuery Query { get; }
        public IReadOnlyList<PaletteResult> Results { get; }

        /// <summary>Problems worth logging (provider exceptions). Never shown as results.</summary>
        public IReadOnlyList<string> Diagnostics { get; }
    }
}
