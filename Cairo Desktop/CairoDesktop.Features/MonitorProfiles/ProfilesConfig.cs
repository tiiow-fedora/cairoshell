using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.MonitorProfiles
{
    /// <summary>One ordered rule: monitors the selector matches get the settings layered on.</summary>
    public sealed class ProfileRule
    {
        public string Name { get; set; }
        public MonitorSelector Match { get; set; } = new MonitorSelector();
        public ProfileSettings Settings { get; set; } = new ProfileSettings();

        /// <summary>The name, or "rule #N" (1-based position) when the rule has none.</summary>
        public string DisplayName(int declarationIndex) =>
            string.IsNullOrWhiteSpace(Name) ? "rule #" + (declarationIndex + 1).ToString(CultureInfo.InvariantCulture) : Name;
    }

    /// <summary>The "monitorProfiles" section: a default profile plus ordered rules.</summary>
    public sealed class ProfilesConfig
    {
        /// <summary>Applied to every monitor above the built-in defaults.</summary>
        public ProfileSettings Default { get; set; } = new ProfileSettings();

        public List<ProfileRule> Rules { get; set; } = new List<ProfileRule>();
    }
}
