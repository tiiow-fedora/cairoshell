using System;
using CairoDesktop.Widgets.Sdk;

namespace CairoPlus.SampleWidget
{
    /// <summary>
    /// Shows how long Windows has been running. Use it in cairo-plus.json with:
    ///   "widgets": { "uptime": { "type": "uptime", "prefix": "up" } }
    ///   "menuBar": { "layout": { "right": ["widget:uptime", ...] } }
    /// </summary>
    public sealed class UptimeWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "uptime";

        public ICairoWidget Create(WidgetContext context) => new UptimeWidget(context);
    }

    internal sealed class UptimeWidget : TextWidget
    {
        private readonly string _prefix;

        public UptimeWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(30))
        {
            // Options come from this widget's object in cairo-plus.json.
            _prefix = context.Options.GetString("prefix", "up");
        }

        protected override string GetText()
        {
            TimeSpan up = TimeSpan.FromMilliseconds(NativeUptime.Milliseconds());
            string text = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m";
            return string.IsNullOrEmpty(_prefix) ? text : $"{_prefix} {text}";
        }

        protected override string GetToolTip() => $"Windows has been running since {DateTime.Now.AddMilliseconds(-NativeUptime.Milliseconds()):g}";
    }

    internal static class NativeUptime
    {
        // System.Environment.TickCount64 is missing on .NET Framework; GetTickCount64 works on every target.
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        public static double Milliseconds() => GetTickCount64();
    }
}
