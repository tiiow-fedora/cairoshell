// Inert until CairoDesktop.Customization defines CAIRO_FEATURES (see docs/features/README.md). Never compiled on Linux.
#if CAIRO_FEATURES
using System;
using System.Globalization;
using CairoDesktop.Features.WorldClock;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.WorldClock
{
    internal sealed class WorldClockWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "worldclock";

        public ICairoWidget Create(WidgetContext context) => new WorldClockWidget(context);
    }

    /// <summary>
    /// Several clocks in one widget. Options: zones ("Tokyo=Asia/Tokyo;NYC=America/New_York"), format, separator, labels,
    /// dayDelta, workStart, workEnd, interval (seconds, default 10).
    /// </summary>
    internal sealed class WorldClockWidget : TextWidget
    {
        private readonly WorldClockFormatter _formatter;
        private bool _reportedProblems;

        public WorldClockWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(Math.Max(1, Math.Min(60, context.Options.GetNumber("interval", 10)))))
        {
            var options = new WorldClockOptions
            {
                Format = context.Options.GetString("format", "HH:mm"),
                Separator = context.Options.GetString("separator", "   "),
                ShowLabels = context.Options.GetBool("labels", true),
                ShowDayDelta = context.Options.GetBool("dayDelta", true),
                WorkStartHour = (int)Math.Max(0, Math.Min(23, context.Options.GetNumber("workStart", 9))),
                WorkEndHour = (int)Math.Max(1, Math.Min(24, context.Options.GetNumber("workEnd", 18))),
            };
            _formatter = new WorldClockFormatter(
                ClockZoneSpec.ParseList(context.Options.GetString("zones", "UTC")),
                options,
                TimeZoneInfo.FindSystemTimeZoneById,
                CultureInfo.CurrentCulture);
        }

        private WorldClockView Render()
        {
            WorldClockView view = _formatter.Render(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            if (!_reportedProblems && view.Problems.Count > 0)
            {
                _reportedProblems = true;
                foreach (string problem in view.Problems) Context.Log(problem);
            }

            return view;
        }

        protected override string GetText() => Render().Text;

        protected override string GetToolTip() => Render().ToolTip;
    }
}
#endif
