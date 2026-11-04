using System.Collections.Generic;
using CairoDesktop.Features.MonitorProfiles;

namespace CairoDesktop.Features.Tests.MonitorProfiles
{
    internal static class Mon
    {
        public static MonitorInfo Make(string name, int x, int y, int w, int h, bool primary = false, double dpi = 1.0, string hw = "")
        {
            return new MonitorInfo
            {
                DeviceName = name,
                IsPrimary = primary,
                Bounds = new MonitorRect(x, y, w, h),
                WorkArea = new MonitorRect(x, y, w, h - 40),
                DpiScale = dpi,
                HardwareId = hw,
            };
        }

        /// <summary>Numbers the monitors like the adapter does and returns them.</summary>
        public static List<MonitorInfo> Set(params MonitorInfo[] monitors) => MonitorInfo.Number(monitors);

        public static MonitorInfo Fhd(string name = @"\\.\DISPLAY1", bool primary = true) => Make(name, 0, 0, 1920, 1080, primary);
        public static MonitorInfo Ultra(string name = @"\\.\DISPLAY2") => Make(name, 1920, 0, 3440, 1440);
        public static MonitorInfo Portrait(string name = @"\\.\DISPLAY3") => Make(name, 5360, 0, 1080, 1920);
    }
}
