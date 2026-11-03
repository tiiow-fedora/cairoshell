using System;
using Microsoft.Win32;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>Reads whether Windows apps are in light or dark mode.</summary>
    public static class WindowsAppearance
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsValue = "AppsUseLightTheme";

        /// <summary>
        /// Turns the registry value into light (true) or dark (false). Windows stores 1 for light and 0 for dark;
        /// the value is missing on Windows versions before 10 and on a fresh profile, which count as light.
        /// </summary>
        public static bool IsLightValue(object registryValue)
        {
            if (registryValue is int number) return number != 0;
            return true;
        }

        /// <summary>True when Windows apps use light mode. Light if it cannot be read.</summary>
        public static bool UsesLightMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    return IsLightValue(key?.GetValue(AppsValue));
                }
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
