using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CairoDesktop.Features.CommandPalette
{
    /// <summary>A user-defined command from the config: shows up as <c>title</c> and runs <c>run</c> with <c>args</c>. Thread-safety: immutable.</summary>
    public sealed class UserCommand
    {
        public UserCommand(string title, string run, string args)
        {
            Title = title ?? "";
            Run = run ?? "";
            Args = args ?? "";
        }

        public string Title { get; }
        public string Run { get; }
        public string Args { get; }
    }

    /// <summary>Which built-in providers are on.</summary>
    public sealed class ProviderToggles
    {
        public bool Commands { get; set; } = true;
        public bool Apps { get; set; } = true;
        public bool Calculator { get; set; } = true;
        public bool WebSearch { get; set; } = true;
    }

    /// <summary>
    /// The <c>commandPalette</c> section of cairo-plus.json after validation.
    /// Thread-safety: plain data; treat as read-only once handed to the engine/controller.
    /// </summary>
    public sealed class PaletteConfig
    {
        public const string DefaultHotkey = "Alt+Space";
        public const string DefaultWebSearchUrl = "https://duckduckgo.com/?q={q}";
        public const int DefaultMaxResults = 8;
        public const int MaxMaxResults = 50;

        public bool Enabled { get; set; } = true;
        public string Hotkey { get; set; } = DefaultHotkey;
        public int MaxResults { get; set; } = DefaultMaxResults;
        public string WebSearchUrl { get; set; } = DefaultWebSearchUrl;
        public ProviderToggles Providers { get; set; } = new ProviderToggles();
        public List<UserCommand> Commands { get; set; } = new List<UserCommand>();
    }

    /// <summary>A parsed config plus a list of human-readable problems. Config is never null. Thread-safety: plain data.</summary>
    public sealed class PaletteConfigResult
    {
        public PaletteConfig Config { get; internal set; } = new PaletteConfig();
        public List<string> Findings { get; } = new List<string>();
    }

    /// <summary>
    /// Parses the palette config leniently (comments, trailing commas, case-insensitive names). Invalid values are
    /// reported in <see cref="PaletteConfigResult.Findings"/> and replaced by defaults; nothing here throws.
    /// Thread-safety: stateless.
    /// </summary>
    public static class PaletteConfigParser
    {
        private const int MaxUserCommands = 200;
        private static readonly JsonDocumentOptions Options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>Parses just the section object, i.e. <c>{ "hotkey": "...", ... }</c>.</summary>
        public static PaletteConfigResult Parse(string sectionJson)
        {
            var result = new PaletteConfigResult();
            if (string.IsNullOrWhiteSpace(sectionJson)) return result;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(sectionJson, Options))
                {
                    ParseSection(doc.RootElement, result);
                }
            }
            catch (JsonException ex)
            {
                result.Config = new PaletteConfig();
                result.Findings.Add("commandPalette: invalid JSON (" + ex.Message + "); using defaults.");
            }

            return result;
        }

        /// <summary>Parses a whole cairo-plus.json and reads its <c>commandPalette</c> property (missing = defaults, no findings).</summary>
        public static PaletteConfigResult ParseFromRoot(string rootJson)
        {
            var result = new PaletteConfigResult();
            if (string.IsNullOrWhiteSpace(rootJson)) return result;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(rootJson, Options))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "commandPalette", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseSection(p.Value, result);
                            break;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                result.Config = new PaletteConfig();
                result.Findings.Add("commandPalette: invalid JSON (" + ex.Message + "); using defaults.");
            }

            return result;
        }

        /// <summary>Why <paramref name="url"/> is not a usable web-search template, or null when it is fine.</summary>
        public static string ValidateWebSearchUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "must not be empty";
            if (url.IndexOf("{q}", StringComparison.Ordinal) < 0) return "must contain {q}";
            // Check the shape with a harmless value substituted.
            if (!PaletteAction.IsSafeWebUrl(url.Replace("{q}", "test"))) return "must be an absolute http or https URL";
            return null;
        }

        /// <summary>
        /// Checks a hotkey like "Ctrl+Alt+Space" and returns the canonical spelling, or null with <paramref name="error"/> set.
        /// Needs at least one modifier (Ctrl, Alt, Shift, Win) and exactly one key (A-Z, 0-9, F1-F24, or a named key).
        /// </summary>
        public static string NormalizeHotkey(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "is empty"; return null; }

            bool ctrl = false, alt = false, shift = false, win = false;
            string key = null;

            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) { error = "has an empty part"; return null; }

                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": ctrl = true; continue;
                    case "alt": alt = true; continue;
                    case "shift": shift = true; continue;
                    case "win": case "windows": case "super": win = true; continue;
                }

                if (key != null) { error = "has more than one key"; return null; }
                key = NormalizeKey(part);
                if (key == null) { error = $"has an unknown key '{part}'"; return null; }
            }

            if (key == null) { error = "has no key"; return null; }
            if (!(ctrl || alt || shift || win)) { error = "needs at least one modifier (Ctrl, Alt, Shift or Win)"; return null; }

            var sb = new StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            if (win) sb.Append("Win+");
            return sb.Append(key).ToString();
        }

        private static readonly string[] NamedKeys = { "Space", "Enter", "Tab", "Escape", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right" };

        private static string NormalizeKey(string part)
        {
            if (part.Length == 1)
            {
                char c = char.ToUpperInvariant(part[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c.ToString();
                return null;
            }

            if ((part[0] == 'F' || part[0] == 'f') && int.TryParse(part.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
                return "F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture);

            foreach (string named in NamedKeys)
            {
                if (string.Equals(named, part, StringComparison.OrdinalIgnoreCase)) return named;
            }

            if (string.Equals(part, "esc", StringComparison.OrdinalIgnoreCase)) return "Escape";
            return null;
        }

        private static void ParseSection(JsonElement section, PaletteConfigResult result)
        {
            var config = new PaletteConfig();
            result.Config = config;

            if (section.ValueKind == JsonValueKind.Null) return;
            if (section.ValueKind != JsonValueKind.Object)
            {
                result.Findings.Add("commandPalette: expected an object; using defaults.");
                return;
            }

            foreach (JsonProperty p in section.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "enabled":
                        if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False) config.Enabled = p.Value.GetBoolean();
                        else result.Findings.Add("commandPalette.enabled: expected true or false.");
                        break;

                    case "hotkey":
                        if (p.Value.ValueKind != JsonValueKind.String) { result.Findings.Add("commandPalette.hotkey: expected a string."); break; }
                        string hk = NormalizeHotkey(p.Value.GetString(), out string hkError);
                        if (hk == null) result.Findings.Add($"commandPalette.hotkey \"{p.Value.GetString()}\" {hkError}; using {PaletteConfig.DefaultHotkey}.");
                        else config.Hotkey = hk;
                        break;

                    case "maxresults":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int max))
                        {
                            if (max < 1 || max > PaletteConfig.MaxMaxResults)
                            {
                                config.MaxResults = Math.Min(PaletteConfig.MaxMaxResults, Math.Max(1, max));
                                result.Findings.Add($"commandPalette.maxResults {max} is out of range 1-{PaletteConfig.MaxMaxResults}; using {config.MaxResults}.");
                            }
                            else config.MaxResults = max;
                        }
                        else result.Findings.Add("commandPalette.maxResults: expected a whole number.");
                        break;

                    case "websearchurl":
                        if (p.Value.ValueKind != JsonValueKind.String) { result.Findings.Add("commandPalette.webSearchUrl: expected a string."); break; }
                        string why = ValidateWebSearchUrl(p.Value.GetString());
                        if (why != null) result.Findings.Add($"commandPalette.webSearchUrl {why}; using {PaletteConfig.DefaultWebSearchUrl}.");
                        else config.WebSearchUrl = p.Value.GetString();
                        break;

                    case "providers":
                        ParseProviders(p.Value, config.Providers, result);
                        break;

                    case "commands":
                        ParseCommands(p.Value, config, result);
                        break;

                    default:
                        result.Findings.Add($"commandPalette.{p.Name}: unknown setting ignored.");
                        break;
                }
            }
        }

        private static void ParseProviders(JsonElement el, ProviderToggles toggles, PaletteConfigResult result)
        {
            if (el.ValueKind != JsonValueKind.Object) { result.Findings.Add("commandPalette.providers: expected an object."); return; }

            foreach (JsonProperty p in el.EnumerateObject())
            {
                bool isBool = p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False;
                string name = p.Name.ToLowerInvariant();
                if (name != "commands" && name != "apps" && name != "calculator" && name != "websearch")
                {
                    result.Findings.Add($"commandPalette.providers.{p.Name}: unknown provider ignored.");
                    continue;
                }

                if (!isBool) { result.Findings.Add($"commandPalette.providers.{p.Name}: expected true or false."); continue; }
                bool v = p.Value.GetBoolean();
                switch (name)
                {
                    case "commands": toggles.Commands = v; break;
                    case "apps": toggles.Apps = v; break;
                    case "calculator": toggles.Calculator = v; break;
                    case "websearch": toggles.WebSearch = v; break;
                }
            }
        }

        private static void ParseCommands(JsonElement el, PaletteConfig config, PaletteConfigResult result)
        {
            if (el.ValueKind != JsonValueKind.Array) { result.Findings.Add("commandPalette.commands: expected an array."); return; }

            int index = 0;
            foreach (JsonElement c in el.EnumerateArray())
            {
                index++;
                if (config.Commands.Count >= MaxUserCommands)
                {
                    result.Findings.Add($"commandPalette.commands: only the first {MaxUserCommands} commands are used.");
                    break;
                }

                if (c.ValueKind != JsonValueKind.Object) { result.Findings.Add($"commandPalette.commands[{index}]: expected an object."); continue; }

                string title = null, run = null, args = null;
                foreach (JsonProperty p in c.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    switch (p.Name.ToLowerInvariant())
                    {
                        case "title": title = p.Value.GetString(); break;
                        case "run": run = p.Value.GetString(); break;
                        case "args": args = p.Value.GetString(); break;
                    }
                }

                if (string.IsNullOrWhiteSpace(title)) { result.Findings.Add($"commandPalette.commands[{index}]: missing \"title\"; skipped."); continue; }
                if (string.IsNullOrWhiteSpace(run)) { result.Findings.Add($"commandPalette.commands[{index}] \"{title}\": missing \"run\"; skipped."); continue; }

                config.Commands.Add(new UserCommand(title.Trim(), run.Trim(), args));
            }
        }
    }
}
