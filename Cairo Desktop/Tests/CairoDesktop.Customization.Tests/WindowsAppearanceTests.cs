using CairoDesktop.Customization.Themes;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class WindowsAppearanceTests
    {
        [Fact]
        public void Registry_value_maps_to_light_or_dark()
        {
            Assert.True(WindowsAppearance.IsLightValue(1));
            Assert.False(WindowsAppearance.IsLightValue(0));
        }

        [Fact]
        public void Missing_or_odd_registry_values_count_as_light()
        {
            Assert.True(WindowsAppearance.IsLightValue(null));
            Assert.True(WindowsAppearance.IsLightValue("0"));
        }
    }
}
