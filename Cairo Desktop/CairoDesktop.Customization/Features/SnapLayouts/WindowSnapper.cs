// Win32 adapter for CairoDesktop.Features.SnapLayouts. NOT compiled or run in CI (needs Windows); all decisions
// live in the tested library, this file only reads window state and calls SetWindowPos.
//
// The whole file sits behind CAIRO_FEATURES because CairoDesktop.Customization does not reference
// CairoDesktop.Features yet. Whoever wires the feature adds the ProjectReference and
// <DefineConstants>$(DefineConstants);CAIRO_FEATURES</DefineConstants>. Until then this compiles to nothing.
#if CAIRO_FEATURES
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CairoDesktop.Features.SnapLayouts;
using SnapRect = CairoDesktop.Features.SnapLayouts.Rect;

namespace CairoDesktop.Customization.Features.SnapLayouts
{
    /// <summary>
    /// Moves the foreground window into layout zones. Every method must be called on the UI thread from the hotkey handler.
    /// Holds no window handles: the only per-window state is SnapMemory, keyed by an opaque string, pruned with IsWindow.
    /// </summary>
    internal sealed class WindowSnapper
    {
        private readonly SnapMemory _memory = new SnapMemory(64);
        private readonly System.Collections.Generic.Dictionary<string, string> _layoutByMonitor =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Handles are needed only so Prune can ask IsWindow; they are dropped together with the memory entry.
        private readonly System.Collections.Generic.Dictionary<string, IntPtr> _handles = new System.Collections.Generic.Dictionary<string, IntPtr>();
        private readonly int _ownPid = Process.GetCurrentProcess().Id;
        private SnapConfig _config = new SnapConfig();
        private LayoutCatalog _catalog = new LayoutCatalog();
        private SnapPolicy _policy = new SnapPolicy();

        public void Configure(SnapConfig config)
        {
            _config = config ?? new SnapConfig();
            _catalog = _config.BuildCatalog();
            _policy = new SnapPolicy { SnapMaximized = _config.SnapMaximized };
            _layoutByMonitor.Clear();
        }

        /// <summary>Runs a hotkey action (snapLeft, snapRight, snapUp, snapDown, nextZone, prevZone, restore, cycleLayout). False = nothing happened.</summary>
        public bool Execute(string action)
        {
            if (!_config.Enabled) return false;
            _memory.Prune(key => IsWindow(_handles.TryGetValue(key, out IntPtr h) ? h : IntPtr.Zero));

            // Assumption: we want physical pixels. Cairo's manifest is per-monitor-DPI aware, but make this thread so as well
            // (Windows 10 1607+; silently skipped elsewhere) so the rectangles below are never DPI-virtualised.
            IntPtr previous = TrySetPerMonitorDpi();
            try
            {
                return ExecuteCore(action);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return false; // a window vanished mid-operation; nothing to do
            }
            finally
            {
                if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
            }
        }

        private bool ExecuteCore(string action)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (!TryDescribe(hwnd, out WindowInfo w)) return false;

            if (string.Equals(action, "restore", StringComparison.OrdinalIgnoreCase)) return Restore(hwnd, w);
            if (!_policy.CanSnap(w.State)) return false;

            bool cycle = string.Equals(action, "cycleLayout", StringComparison.OrdinalIgnoreCase);
            SnapMemory.Entry entry = null;
            bool snapped = _memory.Peek(w.Key, out entry) && _memory.IsStillSnapped(w.Key, w.WindowRect);
            int current = snapped && entry.LayoutId == w.Layout.Id ? entry.ZoneIndex : -1;

            if (cycle)
            {
                w.Layout = _catalog.Cycle(w.Layout.Id);
                _layoutByMonitor[w.MonitorName] = w.Layout.Id;
                w.Zones = ZoneCalculator.Compute(w.Layout, w.WorkArea, _config.Gap, _config.OuterGap);
                current = snapped ? Math.Min(entry.ZoneIndex, w.Zones.Length - 1) : -1; // keep "the same" zone in the new layout
            }

            int target;
            switch ((action ?? "").ToLowerInvariant())
            {
                case "snapleft": target = ZoneHitTester.Navigate(w.Zones, current, Direction.Left, _config.Wrap); break;
                case "snapright": target = ZoneHitTester.Navigate(w.Zones, current, Direction.Right, _config.Wrap); break;
                case "snapup": target = ZoneHitTester.Navigate(w.Zones, current, Direction.Up, _config.Wrap); break;
                case "snapdown": target = ZoneHitTester.Navigate(w.Zones, current, Direction.Down, _config.Wrap); break;
                case "nextzone": target = ZoneHitTester.Next(w.Zones, current, _config.Wrap); break;
                case "prevzone": target = ZoneHitTester.Previous(w.Zones, current, _config.Wrap); break;
                case "cyclelayout": target = snapped ? current : -1; break; // an unsnapped window is left where it is

                default: return false;
            }

            if (target < 0 || target >= w.Zones.Length) return false;
            if (current == target && !cycle) return true; // already there

            // A maximized window ignores SetWindowPos sizes until it is restored (only reached when snapMaximized is on).
            SnapRect before = w.WindowRect;
            if ((w.State & WindowStates.Maximized) != 0)
            {
                ShowWindow(hwnd, SW_RESTORE);
                if (!GetWindowRect(hwnd, out RECT restored)) return false;
                before = restored.ToRect(); // the un-maximized rectangle is what "restore" should go back to
            }

            // GetWindowRect includes the invisible resize border; DWM's extended frame bounds are the visible window.
            SnapRect ext = GetFrame(hwnd, before);
            SnapRect toSet = FrameInsets.Compensate(w.Zones[target], ext, before);
            if (!SetWindowPos(hwnd, IntPtr.Zero, toSet.Left, toSet.Top, toSet.Width, toSet.Height, SWP_NOZORDER | SWP_NOACTIVATE))
            {
                return false; // typically ERROR_ACCESS_DENIED: the window is elevated and we are not (UIPI). Not an error to report.
            }

            GetWindowRect(hwnd, out RECT after); // the window may have clamped to its minimum size; remember what really happened
            _memory.Remember(w.Key, before, after.ToRect(), target, w.Layout.Id);
            _handles[w.Key] = hwnd;
            return true;
        }

        private bool Restore(IntPtr hwnd, WindowInfo w)
        {
            if (!_memory.Peek(w.Key, out SnapMemory.Entry e)) return false;
            SnapRect r = e.OriginalRect;
            if (!SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE)) return false;
            _memory.Forget(w.Key);
            _handles.Remove(w.Key);
            return true;
        }

        private sealed class WindowInfo
        {
            public string Key, MonitorName;
            public SnapRect WindowRect, WorkArea;
            public WindowStates State;
            public SnapLayout Layout;
            public SnapRect[] Zones;
        }

        private bool TryDescribe(IntPtr hwnd, out WindowInfo info)
        {
            info = null;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd)) return false;

            // Never touch the desktop, the shell's own windows, or Cairo's (menu bar, taskbar, desktop overlay).
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)_ownPid) return false;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            switch (cls.ToString())
            {
                case "Progman": case "WorkerW": case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd": return false;
            }

            // A hung window would block SetWindowPos (it sends WM_WINDOWPOSCHANGING to the target's thread).
            if (IsHungAppWindow(hwnd)) return false;
            if (!GetWindowRect(hwnd, out RECT rect)) return false;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi)) return false;

            int style = GetWindowLong(hwnd, GWL_STYLE);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            WindowStates state = WindowStates.Normal;
            if (IsZoomed(hwnd)) state |= WindowStates.Maximized;
            if (IsIconic(hwnd)) state |= WindowStates.Minimized;
            if ((style & WS_THICKFRAME) == 0 || (exStyle & WS_EX_TOOLWINDOW) != 0) state |= WindowStates.Excluded; // can't be resized / is a palette
            if (rect.ToRect().Equals(mi.rcMonitor.ToRect()) && (state & WindowStates.Maximized) == 0) state |= WindowStates.FullScreen;

            var desc = new MonitorDescriptor(mi.szDevice, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top, (mi.dwFlags & 1) != 0);
            _layoutByMonitor.TryGetValue(desc.DeviceName, out string chosen);
            SnapLayout layout = LayoutPicker.Pick(_catalog, desc, _config.MonitorLayouts, chosen ?? _config.DefaultLayout);
            SnapRect work = mi.rcWork.ToRect();

            info = new WindowInfo
            {
                // Process name guards against handle reuse; the title is left out on purpose so retitled windows keep their memory.
                Key = WindowKey.Make(hwnd.ToInt64(), ProcessName((int)pid)),
                MonitorName = desc.DeviceName, WindowRect = rect.ToRect(), WorkArea = work, State = state, Layout = layout,
                Zones = ZoneCalculator.Compute(layout, work, _config.Gap, _config.OuterGap)
            };
            return true;
        }

        private static string ProcessName(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch (Exception) { return "pid" + pid; } // exited, or access denied
        }

        private static SnapRect GetFrame(IntPtr hwnd, SnapRect fallback)
        {
            // Fails (returns non-zero) with DWM composition off; Compensate treats an empty rect as "no invisible border".
            return DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf(typeof(RECT))) == 0 ? r.ToRect() : fallback;
        }

        private static IntPtr TrySetPerMonitorDpi()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }

        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WS_THICKFRAME = 0x00040000, WS_EX_TOOLWINDOW = 0x80;
        private const int SW_RESTORE = 9, DWMWA_EXTENDED_FRAME_BOUNDS = 9, MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
            public SnapRect ToRect() => new SnapRect(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
    }
}
#endif
