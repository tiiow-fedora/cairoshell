using System;
using System.IO;
using System.Text.Json;

namespace CairoDesktop.Customization.Config
{
    public sealed class ConfigLoadResult
    {
        /// <summary>The parsed config. Never null: a missing file yields a disabled default config.</summary>
        public CairoPlusConfig Config { get; internal set; }

        public bool FileExists { get; internal set; }

        /// <summary>Parse error, or null on success. When set, <see cref="Config"/> is a disabled default.</summary>
        public string Error { get; internal set; }

        public bool Success => Error == null;
    }

    /// <summary>
    /// Reads cairo-plus.json and theme manifests. Both are JSON with comments and trailing commas allowed.
    /// </summary>
    public static class ConfigLoader
    {
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ConfigLoadResult LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new ConfigLoadResult { Config = new CairoPlusConfig(), FileExists = false };
            }

            string text;
            try
            {
                text = ReadAllTextShared(path);
            }
            catch (Exception ex)
            {
                return new ConfigLoadResult { Config = new CairoPlusConfig(), FileExists = true, Error = ex.Message };
            }

            var result = Parse(text);
            result.FileExists = true;
            return result;
        }

        public static ConfigLoadResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                // An empty file is treated like a missing one rather than as an error.
                return new ConfigLoadResult { Config = new CairoPlusConfig() };
            }

            try
            {
                var config = JsonSerializer.Deserialize<CairoPlusConfig>(json, JsonOptions) ?? new CairoPlusConfig();
                return new ConfigLoadResult { Config = config };
            }
            catch (JsonException ex)
            {
                return new ConfigLoadResult { Config = new CairoPlusConfig(), Error = DescribeJsonError(ex) };
            }
        }

        public static T Deserialize<T>(string json) where T : class
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        internal static string DescribeJsonError(JsonException ex)
        {
            if (ex.LineNumber.HasValue)
            {
                return $"line {ex.LineNumber + 1}, column {ex.BytePositionInLine + 1}: {FirstSentence(ex.Message)}";
            }

            return FirstSentence(ex.Message);
        }

        private static string FirstSentence(string message)
        {
            int idx = message.IndexOf(" Path:", StringComparison.Ordinal);
            return idx > 0 ? message.Substring(0, idx) : message;
        }

        /// <summary>Reads a file that an editor may still have open for writing.</summary>
        internal static string ReadAllTextShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
