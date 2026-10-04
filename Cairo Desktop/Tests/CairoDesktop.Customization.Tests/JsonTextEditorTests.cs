using CairoDesktop.Customization.Config;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class JsonTextEditorTests
    {
        [Fact]
        public void Replaces_existing_value_and_keeps_comments()
        {
            const string json = "// my config\n{\n  // which theme\n  \"theme\": \"Nord\", // keep me\n  \"enabled\": false,\n}";

            string updated = JsonTextEditor.SetString(json, "theme", "Gruvbox");

            Assert.Contains("\"theme\": \"Gruvbox\", // keep me", updated);
            Assert.Contains("// my config", updated);
            Assert.Contains("// which theme", updated);
            Assert.Equal("Gruvbox", ConfigLoader.Parse(updated).Config.Theme);
        }

        [Fact]
        public void Replaces_null_and_bool_values()
        {
            string json = "{ \"enabled\": false, \"theme\": null }";

            json = JsonTextEditor.SetBool(json, "enabled", true);
            json = JsonTextEditor.SetString(json, "theme", "TokyoNight");

            var config = ConfigLoader.Parse(json).Config;
            Assert.True(config.Enabled);
            Assert.Equal("TokyoNight", config.Theme);
        }

        [Fact]
        public void Replaces_whole_object_values()
        {
            string json = "{ \"theme\": { \"nested\": [1, 2, { \"a\": 3 }] }, \"enabled\": true }";

            string updated = JsonTextEditor.SetString(json, "theme", "Nord");

            Assert.Equal("{ \"theme\": \"Nord\", \"enabled\": true }", updated);
        }

        [Fact]
        public void Ignores_nested_properties_with_the_same_name()
        {
            string json = "{ \"widgets\": { \"theme\": \"x\" }, \"theme\": \"Nord\" }";

            string updated = JsonTextEditor.SetString(json, "theme", "Gruvbox");

            Assert.Contains("\"widgets\": { \"theme\": \"x\" }", updated);
            Assert.Contains("\"theme\": \"Gruvbox\"", updated);
        }

        [Fact]
        public void Inserts_missing_property()
        {
            string updated = JsonTextEditor.SetString("{\n  \"enabled\": true\n}", "theme", "Nord");

            var config = ConfigLoader.Parse(updated).Config;
            Assert.True(config.Enabled);
            Assert.Equal("Nord", config.Theme);
        }

        [Fact]
        public void Inserts_into_empty_object_and_empty_text()
        {
            Assert.Equal("Nord", ConfigLoader.Parse(JsonTextEditor.SetString("{}", "theme", "Nord")).Config.Theme);
            Assert.Equal("Nord", ConfigLoader.Parse(JsonTextEditor.SetString("", "theme", "Nord")).Config.Theme);
        }

        [Fact]
        public void Escapes_string_values()
        {
            string updated = JsonTextEditor.SetString("{}", "theme", "My \"Quoted\" Theme\\");

            Assert.Equal("My \"Quoted\" Theme\\", ConfigLoader.Parse(updated).Config.Theme);
        }
    }
}
