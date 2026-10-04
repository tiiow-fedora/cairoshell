using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Widgets
{
    /// <summary>
    /// A label, a percentage and a small bar meter. "style": "text", "bar" or "both" (default).
    /// </summary>
    internal abstract class MeterWidget : ICairoWidget
    {
        private const double TrackWidth = 34;

        private readonly StackPanel _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _text = new TextBlock();
        private readonly Border _track = new Border { Width = TrackWidth, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Width = 0 };
        private readonly DispatcherTimer _timer;
        private readonly string _label;
        private bool _reportedError;

        protected WidgetContext Context { get; }

        protected MeterWidget(WidgetContext context, string defaultLabel, double defaultIntervalSeconds)
        {
            Context = context;
            _label = context.Options.GetString("label", defaultLabel);

            WidgetTheme.ApplyText(_text, context.Bar);
            _track.SetResourceReference(Border.BackgroundProperty, WidgetTheme.TrackBrushKey);
            _fill.SetResourceReference(Border.BackgroundProperty, WidgetTheme.AccentBrushKey);
            _track.Child = _fill;

            string style = context.Options.GetString("style", "both").ToLowerInvariant();
            if (style != "bar") _panel.Children.Add(_text);
            if (style != "text")
            {
                _track.Margin = new Thickness(style == "bar" ? 0 : 6, 1, 0, 0);
                _panel.Children.Add(_track);
            }

            double seconds = Math.Max(0.5, context.Options.GetNumber("interval", defaultIntervalSeconds));
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(seconds), DispatcherPriority.Background, (s, e) => Refresh(), context.Dispatcher);
            _timer.Start();
            Refresh();
        }

        public FrameworkElement View => _panel;

        /// <summary>Returns 0..100, or null if unavailable.</summary>
        protected abstract double? Sample(out string toolTip);

        private void Refresh()
        {
            try
            {
                double? percent = Sample(out string toolTip);
                if (percent == null)
                {
                    _text.Text = _label + " –";
                    _fill.Width = 0;
                }
                else
                {
                    double p = Math.Max(0, Math.Min(100, percent.Value));
                    _text.Text = string.IsNullOrEmpty(_label) ? $"{p:0}%" : $"{_label} {p:0}%";
                    _fill.Width = TrackWidth * p / 100;
                }

                _panel.ToolTip = toolTip;
            }
            catch (Exception ex)
            {
                _text.Text = _label + " ⚠";
                if (!_reportedError)
                {
                    _reportedError = true;
                    Context.Log(ex.Message);
                }
            }
        }

        public virtual void Dispose()
        {
            _timer.Stop();
        }
    }
}
