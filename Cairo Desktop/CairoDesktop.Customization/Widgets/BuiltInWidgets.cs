using System;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Widgets
{
    #region Clock
    /// <summary>
    /// "clock": "format" (.NET date format, default "ddd h:mm tt"), "tooltipFormat" (default "D"), "interval" seconds (default 1).
    /// </summary>
    internal sealed class ClockWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "clock";

        public ICairoWidget Create(WidgetContext context) => new ClockWidget(context);

        private sealed class ClockWidget : TextWidget
        {
            private readonly string _format;
            private readonly string _toolTipFormat;

            public ClockWidget(WidgetContext context)
                : base(context, TimeSpan.FromSeconds(Math.Max(0.2, context.Options.GetNumber("interval", 1))))
            {
                _format = context.Options.GetString("format", "ddd h:mm tt");
                _toolTipFormat = context.Options.GetString("tooltipFormat", "D");
            }

            protected override string GetText() => DateTime.Now.ToString(_format, CultureInfo.CurrentCulture);

            protected override string GetToolTip() => string.IsNullOrEmpty(_toolTipFormat) ? null : DateTime.Now.ToString(_toolTipFormat, CultureInfo.CurrentCulture);
        }
    }
    #endregion

    #region CPU and memory
    /// <summary>"cpu": total CPU usage. Options: "label" (default "CPU"), "style" (text|bar|both), "interval" (default 2).</summary>
    internal sealed class CpuWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "cpu";

        public ICairoWidget Create(WidgetContext context) => new CpuWidget(context);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        private sealed class CpuWidget : MeterWidget
        {
            private long _lastIdle, _lastTotal;

            public CpuWidget(WidgetContext context) : base(context, "CPU", 2) { }

            protected override double? Sample(out string toolTip)
            {
                toolTip = $"CPU usage ({Environment.ProcessorCount} logical processors)";
                if (!GetSystemTimes(out long idle, out long kernel, out long user)) return null;

                // Kernel time includes idle time.
                long total = kernel + user;
                long idleDelta = idle - _lastIdle, totalDelta = total - _lastTotal;
                _lastIdle = idle;
                _lastTotal = total;

                return totalDelta <= 0 ? 0 : 100.0 * (totalDelta - idleDelta) / totalDelta;
            }
        }
    }

    /// <summary>"memory": physical memory in use. Options: "label" (default "RAM"), "style" (text|bar|both), "interval" (default 2).</summary>
    internal sealed class MemoryWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "memory";

        public ICairoWidget Create(WidgetContext context) => new MemoryWidget(context);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private sealed class MemoryWidget : MeterWidget
        {
            public MemoryWidget(WidgetContext context) : base(context, "RAM", 2) { }

            protected override double? Sample(out string toolTip)
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (!GlobalMemoryStatusEx(ref status))
                {
                    toolTip = null;
                    return null;
                }

                double totalGb = status.ullTotalPhys / 1073741824.0;
                double usedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1073741824.0;
                toolTip = $"Memory: {usedGb:0.0} GB of {totalGb:0.0} GB in use";
                return 100.0 * (status.ullTotalPhys - status.ullAvailPhys) / status.ullTotalPhys;
            }
        }
    }
    #endregion

    #region Battery
    /// <summary>
    /// "battery": charge level. Options: "format" (default "{label} {percent}%"; also {time}),
    /// "hideWhenNoBattery" (default true), "interval" (default 30).
    /// </summary>
    internal sealed class BatteryWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "battery";

        public ICairoWidget Create(WidgetContext context) => new BatteryWidget(context);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private sealed class BatteryWidget : TextWidget
        {
            private readonly string _format;
            private readonly bool _hideWhenNoBattery;
            private string _toolTip;

            public BatteryWidget(WidgetContext context)
                : base(context, TimeSpan.FromSeconds(Math.Max(1, context.Options.GetNumber("interval", 30))))
            {
                _format = context.Options.GetString("format", "{label} {percent}%");
                _hideWhenNoBattery = context.Options.GetBool("hideWhenNoBattery", true);
            }

            protected override string GetText()
            {
                if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS s))
                {
                    _toolTip = null;
                    return _hideWhenNoBattery ? null : "BAT ?";
                }

                bool noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255 || s.BatteryLifePercent == 255;
                if (noBattery)
                {
                    _toolTip = "No battery detected (running on AC power)";
                    return _hideWhenNoBattery ? null : "AC";
                }

                bool charging = (s.BatteryFlag & 8) != 0;
                bool onAc = s.ACLineStatus == 1;
                string time = s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime).ToString(@"h\:mm") : "";
                _toolTip = charging ? "Charging" : onAc ? "Plugged in" : time.Length > 0 ? $"{time} remaining" : "On battery";

                return _format
                    .Replace("{label}", charging ? "CHG" : onAc ? "AC" : "BAT")
                    .Replace("{percent}", s.BatteryLifePercent.ToString(CultureInfo.CurrentCulture))
                    .Replace("{time}", time);
            }

            protected override string GetToolTip() => _toolTip;
        }
    }
    #endregion

    #region Network
    /// <summary>
    /// "network": download/upload speed across active adapters. Options: "format" (default "↓ {down}  ↑ {up}"),
    /// "interface" (substring of an adapter name; default all), "interval" (default 2).
    /// </summary>
    internal sealed class NetworkWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "network";

        public ICairoWidget Create(WidgetContext context) => new NetworkWidget(context);

        private sealed class NetworkWidget : TextWidget
        {
            private readonly string _format;
            private readonly string _interfaceFilter;
            private long _lastReceived = -1, _lastSent;
            private DateTime _lastTime;
            private string _toolTip;

            public NetworkWidget(WidgetContext context)
                : base(context, TimeSpan.FromSeconds(Math.Max(0.5, context.Options.GetNumber("interval", 2))))
            {
                _format = context.Options.GetString("format", "↓ {down}  ↑ {up}");
                _interfaceFilter = context.Options.GetString("interface");
            }

            protected override string GetText()
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                                (string.IsNullOrEmpty(_interfaceFilter) ||
                                 n.Name.IndexOf(_interfaceFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 n.Description.IndexOf(_interfaceFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                long received = 0, sent = 0;
                foreach (var adapter in adapters)
                {
                    var stats = adapter.GetIPStatistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }

                DateTime now = DateTime.UtcNow;
                double down = 0, up = 0;
                if (_lastReceived >= 0)
                {
                    double seconds = Math.Max(0.001, (now - _lastTime).TotalSeconds);
                    down = Math.Max(0, received - _lastReceived) / seconds;
                    up = Math.Max(0, sent - _lastSent) / seconds;
                }

                _lastReceived = received;
                _lastSent = sent;
                _lastTime = now;
                _toolTip = adapters.Count == 0 ? "No active network adapters" : string.Join("\n", adapters.Select(a => a.Name));

                return _format.Replace("{down}", FormatRate(down)).Replace("{up}", FormatRate(up));
            }

            protected override string GetToolTip() => _toolTip;

            internal static string FormatRate(double bytesPerSecond)
            {
                if (bytesPerSecond >= 1048576) return (bytesPerSecond / 1048576).ToString("0.0", CultureInfo.CurrentCulture) + " MB/s";
                if (bytesPerSecond >= 1024) return (bytesPerSecond / 1024).ToString("0", CultureInfo.CurrentCulture) + " KB/s";
                return bytesPerSecond.ToString("0", CultureInfo.CurrentCulture) + " B/s";
            }
        }
    }
    #endregion
}
