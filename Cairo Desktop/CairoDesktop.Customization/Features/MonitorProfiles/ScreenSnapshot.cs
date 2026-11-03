using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CairoDesktop.Features.MonitorProfiles;
using Screen = System.Windows.Forms.Screen;

namespace CairoDesktop.Customization.Features.MonitorProfiles
{
    /// <summary>
    /// Converts the monitors Windows reports into the library's plain <see cref="MonitorInfo"/> records.
    /// Bounds are physical pixels only when the process is per-monitor DPI aware (Cairo's manifest asks for that);
    /// otherwise Windows virtualises them and the sizes selectors see are scaled.
    /// </summary>
    public static class ScreenSnapshot
    {
        /// <summary>The current monitors, numbered like the existing per-monitor overrides (primary is 1). Empty if Windows cannot be queried.</summary>
        public static List<MonitorInfo> Capture()
        {
            var list = new List<MonitorInfo>();
            Screen[] screens;
            try
            {
                screens = Screen.AllScreens;
            }
            catch (Exception)
            {
                return list;
            }

            foreach (Screen screen in screens)
            {
                list.Add(FromScreen(screen));
            }

            return MonitorInfo.Number(list);
        }

        public static MonitorInfo FromScreen(Screen screen)
        {
            return new MonitorInfo
            {
                DeviceName = screen.DeviceName ?? "",
                IsPrimary = screen.Primary,
                Bounds = new MonitorRect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                WorkArea = new MonitorRect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height),
                DpiScale = GetDpiScale(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2),
            };
        }

        private static double GetDpiScale(int x, int y)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                {
                    return dpiX / 96.0;
                }
            }
            catch (DllNotFoundException)
            {
                // shcore.dll is missing before Windows 8.1.
            }
            catch (EntryPointNotFoundException)
            {
            }

            return 1.0;
        }

        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
