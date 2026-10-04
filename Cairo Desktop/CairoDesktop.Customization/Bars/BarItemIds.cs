using System.Collections.Generic;
using System.Windows;

namespace CairoDesktop.Customization.Bars
{
    /// <summary>Layout ids for Cairo's built-in bar items.</summary>
    public static class BarItemIds
    {
        private static readonly Dictionary<string, string> MenuExtraIds = new Dictionary<string, string>
        {
            ["SystemTray"] = "tray",
            ["Volume"] = "volume",
            ["ActionCenter"] = "actionCenter",
            ["Clock"] = "clock",
            ["Search"] = "search"
        };

        /// <summary>
        /// Id for a menu extra control: Cairo's own extras have short names; third-party extension
        /// controls use their class name with a lowercase first letter (e.g. "WeatherExtra" becomes "weatherExtra").
        /// </summary>
        public static string ForMenuExtra(FrameworkElement control)
        {
            string typeName = control.GetType().Name;
            if (MenuExtraIds.TryGetValue(typeName, out string id)) return id;
            return typeName.Length == 0 ? typeName : char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
        }
    }
}
