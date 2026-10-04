using System;
using System.Text;
using System.Text.Json;

namespace CairoDesktop.Customization.Config
{
    /// <summary>
    /// Changes a single top-level property in a JSON-with-comments document without
    /// re-serializing it, so the user's comments and formatting survive.
    /// </summary>
    public static class JsonTextEditor
    {
        private static readonly JsonReaderOptions ReaderOptions = new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string SetString(string json, string property, string value)
        {
            return SetRaw(json, property, value == null ? "null" : JsonSerializer.Serialize(value));
        }

        public static string SetBool(string json, string property, bool value)
        {
            return SetRaw(json, property, value ? "true" : "false");
        }

        /// <summary>Sets <paramref name="property"/> (case-insensitive) to the raw JSON <paramref name="rawValue"/>.</summary>
        public static string SetRaw(string json, string property, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "{\r\n  " + JsonSerializer.Serialize(property) + ": " + rawValue + "\r\n}\r\n";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json.TrimStart('﻿'));
            var reader = new Utf8JsonReader(bytes, ReaderOptions);

            long objectStart = -1;
            bool hasProperties = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 0)
                {
                    objectStart = reader.TokenStartIndex;
                    continue;
                }

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                hasProperties = true;
                bool match = string.Equals(reader.GetString(), property, StringComparison.OrdinalIgnoreCase);

                reader.Read();
                long valueStart = reader.TokenStartIndex;
                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Skip();
                }

                if (match)
                {
                    long valueEnd = reader.BytesConsumed;
                    return Splice(bytes, valueStart, valueEnd, rawValue);
                }
            }

            if (objectStart < 0)
            {
                throw new FormatException("The config file does not contain a JSON object.");
            }

            string insert = "\r\n  " + JsonSerializer.Serialize(property) + ": " + rawValue + (hasProperties ? "," : "\r\n");
            return Splice(bytes, objectStart + 1, objectStart + 1, insert);
        }

        private static string Splice(byte[] bytes, long start, long end, string replacement)
        {
            string before = Encoding.UTF8.GetString(bytes, 0, (int)start);
            string after = Encoding.UTF8.GetString(bytes, (int)end, bytes.Length - (int)end);
            return before + replacement + after;
        }
    }
}
