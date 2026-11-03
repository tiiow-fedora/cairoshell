using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>One row for a list of findings: what was found, where, and how bad it is.</summary>
    public sealed class FindingLine
    {
        public IssueSeverity Severity { get; }

        public string Location { get; }

        public string Message { get; }

        public FindingLine(ThemeIssue issue)
        {
            Severity = issue.Severity;
            Location = issue.Location ?? "";
            Message = issue.Message;
        }

        /// <summary>"Error", "Warning" or "Info".</summary>
        public string SeverityLabel => Severity.ToString();

        /// <summary>The finding on one line, such as "Warning  palette.text: text on background has a contrast ratio of 3.1:1".</summary>
        public string Text => Location.Length == 0 ? $"{SeverityLabel}  {Message}" : $"{SeverityLabel}  {Location}: {Message}";
    }

    /// <summary>
    /// What the theme validator found for one pack, arranged for display (for example in the Theme Packs tab):
    /// counts, a one-line summary, and the findings ordered by severity. Pure data, no UI types.
    /// </summary>
    public sealed class ThemeFindings
    {
        public string PackId { get; }

        public string PackName { get; }

        /// <summary>All findings, errors first, then warnings, then info; original order is kept within a severity.</summary>
        public IReadOnlyList<FindingLine> Lines { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public int InfoCount { get; }

        public ThemeFindings(string packId, string packName, IEnumerable<ThemeIssue> issues)
        {
            PackId = packId;
            PackName = packName;

            var all = (issues ?? Enumerable.Empty<ThemeIssue>()).ToList();
            Lines = all
                .Select((issue, index) => new { issue, index })
                .OrderBy(x => (int)x.issue.Severity)
                .ThenBy(x => x.index)
                .Select(x => new FindingLine(x.issue))
                .ToList();

            ErrorCount = all.Count(i => i.Severity == IssueSeverity.Error);
            WarningCount = all.Count(i => i.Severity == IssueSeverity.Warning);
            InfoCount = all.Count(i => i.Severity == IssueSeverity.Info);
        }

        public static ThemeFindings For(ThemePack pack, ThemeValidationOptions options = null)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            return new ThemeFindings(pack.Id, pack.DisplayName, ThemeValidator.Validate(pack, options));
        }

        /// <summary>True when there is nothing to fix (info notes don't count).</summary>
        public bool IsClean => ErrorCount == 0 && WarningCount == 0;

        public bool HasErrors => ErrorCount > 0;

        /// <summary>"No problems", or for example "1 error, 2 warnings". Notes are mentioned only when that is all there is.</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (ErrorCount > 0) parts.Add(Count(ErrorCount, "error"));
                if (WarningCount > 0) parts.Add(Count(WarningCount, "warning"));
                if (parts.Count > 0) return string.Join(", ", parts);
                return InfoCount > 0 ? "No problems (" + Count(InfoCount, "note") + ")" : "No problems";
            }
        }

        /// <summary>The findings as lines of text, without the notes unless asked for. One header line, then one line per finding.</summary>
        public string ToText(bool includeInfo = false)
        {
            var text = new StringBuilder();
            text.Append(PackName).Append(": ").Append(Summary);

            foreach (var line in Lines)
            {
                if (line.Severity == IssueSeverity.Info && !includeInfo) continue;
                text.Append('\n').Append(line.Text);
            }

            return text.ToString();
        }

        private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
