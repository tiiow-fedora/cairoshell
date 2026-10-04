using System;
using System.Globalization;
using System.Text.Json;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Widgets
{
    /// <summary><see cref="IWidgetOptions"/> over a widget's JSON object from cairo-plus.json.</summary>
    internal sealed class JsonWidgetOptions : IWidgetOptions
    {
        private readonly JsonElement _element;
        private readonly bool _isObject;

        public JsonWidgetOptions(JsonElement? element)
        {
            _isObject = element.HasValue && element.Value.ValueKind == JsonValueKind.Object;
            _element = _isObject ? element.Value : default;
        }

        public static readonly JsonWidgetOptions Empty = new JsonWidgetOptions(null);

        private bool TryGet(string name, out JsonElement value)
        {
            value = default;
            if (!_isObject) return false;

            foreach (var property in _element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        public bool Has(string name) => TryGet(name, out _);

        public string GetString(string name, string defaultValue = null)
        {
            if (!TryGet(name, out JsonElement value)) return defaultValue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return defaultValue;
            }
        }

        public double GetNumber(string name, double defaultValue = 0)
        {
            if (!TryGet(name, out JsonElement value)) return defaultValue;
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return defaultValue;
        }

        public bool GetBool(string name, bool defaultValue = false)
        {
            if (!TryGet(name, out JsonElement value)) return defaultValue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)) return parsed;
            return defaultValue;
        }
    }
}
