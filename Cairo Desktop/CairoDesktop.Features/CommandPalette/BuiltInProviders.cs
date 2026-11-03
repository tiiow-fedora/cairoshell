using System;
using System.Collections.Generic;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>Items for the user commands from the config. Thread-safety: immutable.</summary>
    public sealed class UserCommandProvider : ICommandProvider
    {
        private readonly PaletteItem[] _items;

        public UserCommandProvider(IEnumerable<UserCommand> commands)
        {
            var list = new List<PaletteItem>();
            int n = 0;
            if (commands != null)
            {
                foreach (UserCommand c in commands)
                {
                    n++;
                    if (c == null || string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.Run)) continue;
                    list.Add(new PaletteItem("cmd:" + c.Title.Trim() + "#" + n, c.Title.Trim(), c.Run,
                        PaletteCategories.Command, null, PaletteAction.Launch(c.Run, c.Args).ToPayload()));
                }
            }
            _items = list.ToArray();
        }

        public string Name => "Commands";

        public IReadOnlyList<PaletteItem> Query(PaletteQuery query) => _items;
    }

    /// <summary>
    /// Answers "=expression" with the result. In <see cref="PaletteMode.All"/> it only answers when the text looks like
    /// maths (a digit plus an operator or parenthesis) and evaluates cleanly, so ordinary searches stay quiet.
    /// Thread-safety: stateless.
    /// </summary>
    public sealed class CalculatorProvider : ICommandProvider
    {
        public const int PinnedScore = 10000;

        public string Name => "Calculator";

        public IReadOnlyList<PaletteItem> Query(PaletteQuery query)
        {
            if (query == null || query.Text.Length == 0) return Array.Empty<PaletteItem>();
            bool explicitMode = query.Mode == PaletteMode.Calculator;
            if (!explicitMode && (query.Mode != PaletteMode.All || !LooksLikeMath(query.Text))) return Array.Empty<PaletteItem>();

            CalcResult r = Calculator.Evaluate(query.Text);
            if (!r.Success)
            {
                if (!explicitMode) return Array.Empty<PaletteItem>();
                return new[] { new PaletteItem("calc:error", r.Error, query.Text, PaletteCategories.Calculator, null, "", PinnedScore) };
            }

            string text = r.Format();
            return new[] { new PaletteItem("calc:result", text, query.Text + "  (Enter copies the result)", PaletteCategories.Calculator, null, PaletteAction.Copy(text).ToPayload(), PinnedScore) };
        }

        internal static bool LooksLikeMath(string text)
        {
            bool digit = false, op = false;
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9') digit = true;
                else if ("+-*/%^(".IndexOf(c) >= 0) op = true;
            }
            return digit && op;
        }
    }

    /// <summary>
    /// Offers "Search the web for ..." in <c>?</c> mode, using the configured URL template with <c>{q}</c> replaced by the
    /// URL-encoded text. Thread-safety: immutable.
    /// </summary>
    public sealed class WebSearchProvider : ICommandProvider
    {
        public const int PinnedScore = 9000;
        private readonly string _template;

        /// <param name="urlTemplate">Must pass <see cref="PaletteConfigParser.ValidateWebSearchUrl"/>; otherwise the default is used.</param>
        public WebSearchProvider(string urlTemplate)
        {
            _template = PaletteConfigParser.ValidateWebSearchUrl(urlTemplate) == null ? urlTemplate : PaletteConfig.DefaultWebSearchUrl;
        }

        public string Name => "Web search";

        public IReadOnlyList<PaletteItem> Query(PaletteQuery query)
        {
            if (query == null || query.Mode != PaletteMode.Web || query.Text.Length == 0) return Array.Empty<PaletteItem>();

            string url = BuildUrl(query.Text);
            return new[] { new PaletteItem("web:search", "Search the web for \"" + query.Text + "\"", url, PaletteCategories.Web, null, PaletteAction.Url(url).ToPayload(), PinnedScore) };
        }

        internal string BuildUrl(string text) => _template.Replace("{q}", Uri.EscapeDataString(text));
    }
}
