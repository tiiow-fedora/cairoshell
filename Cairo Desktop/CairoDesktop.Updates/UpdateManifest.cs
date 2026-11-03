using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CairoDesktop.Updates
{
    /// <summary>One downloadable file of a release, e.g. the x64 installer.</summary>
    public sealed class UpdateAsset
    {
        /// <summary>Which build this is for: "x64", "x86", "arm64", optionally with "-net480".</summary>
        public string Platform { get; set; } = "";
        /// <summary>"installer" or "portable".</summary>
        public string Kind { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>Lowercase hex SHA-256 of the file.</summary>
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }

    public sealed class UpdateManifest
    {
        public int SchemaVersion { get; set; }
        public SemanticVersion Version { get; set; } = null!;
        /// <summary>"stable" or "beta".</summary>
        public string Channel { get; set; } = "stable";
        public DateTimeOffset Published { get; set; }
        /// <summary>Optional: installs older than this must update (they are told the update is required).</summary>
        public SemanticVersion? MinimumVersion { get; set; }
        public string? NotesUrl { get; set; }
        public List<UpdateAsset> Assets { get; } = new List<UpdateAsset>();

        public const int SupportedSchemaVersion = 1;

        /// <summary>
        /// Parses manifest JSON. Throws <see cref="UpdateException"/> for anything malformed.
        /// Call only on bytes whose signature has already been verified.
        /// </summary>
        public static UpdateManifest Parse(byte[] json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new UpdateException("Manifest root must be an object.");

                var m = new UpdateManifest();
                m.SchemaVersion = RequireInt(root, "schemaVersion");
                if (m.SchemaVersion != SupportedSchemaVersion)
                    throw new UpdateException("Unsupported manifest schema " + m.SchemaVersion + ".");

                m.Version = ParseVersion(RequireString(root, "version"), "version");
                if (root.TryGetProperty("channel", out var ch) && ch.ValueKind == JsonValueKind.String)
                    m.Channel = ch.GetString()!.ToLowerInvariant();
                if (!DateTimeOffset.TryParse(RequireString(root, "published"), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var published))
                    throw new UpdateException("Manifest 'published' is not a date.");
                m.Published = published;

                if (root.TryGetProperty("minimumVersion", out var min) && min.ValueKind == JsonValueKind.String)
                    m.MinimumVersion = ParseVersion(min.GetString()!, "minimumVersion");
                if (root.TryGetProperty("notesUrl", out var notes) && notes.ValueKind == JsonValueKind.String)
                    m.NotesUrl = notes.GetString();

                if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                    throw new UpdateException("Manifest has no 'assets' array.");
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) throw new UpdateException("Asset must be an object.");
                    var asset = new UpdateAsset
                    {
                        Platform = RequireString(a, "platform").ToLowerInvariant(),
                        Kind = RequireString(a, "kind").ToLowerInvariant(),
                        Url = RequireString(a, "url"),
                        Sha256 = RequireString(a, "sha256").ToLowerInvariant(),
                        Size = RequireLong(a, "size"),
                    };
                    if (!FileHashVerifier.IsValidHex(asset.Sha256))
                        throw new UpdateException("Asset sha256 must be 64 hex characters.");
                    if (asset.Size <= 0) throw new UpdateException("Asset size must be positive.");
                    if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        throw new UpdateException("Asset url must be an absolute https URL.");
                    m.Assets.Add(asset);
                }
                if (m.Assets.Count == 0) throw new UpdateException("Manifest lists no assets.");
                return m;
            }
            catch (JsonException ex)
            {
                throw new UpdateException("Manifest is not valid JSON: " + ex.Message);
            }
        }

        private static SemanticVersion ParseVersion(string text, string field)
        {
            if (!SemanticVersion.TryParse(text, out var v)) throw new UpdateException("Manifest '" + field + "' is not a version.");
            return v;
        }

        private static string RequireString(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(p.GetString()))
                throw new UpdateException("Manifest is missing string field '" + name + "'.");
            return p.GetString()!;
        }

        private static int RequireInt(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out int v))
                throw new UpdateException("Manifest is missing integer field '" + name + "'.");
            return v;
        }

        private static long RequireLong(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt64(out long v))
                throw new UpdateException("Manifest is missing integer field '" + name + "'.");
            return v;
        }
    }

    /// <summary>Raised for any verification or format failure. The message is safe to show to a user.</summary>
    public class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
        public UpdateException(string message, Exception inner) : base(message, inner) { }
    }
}
