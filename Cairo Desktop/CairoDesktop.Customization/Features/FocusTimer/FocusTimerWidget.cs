// Inert until CairoDesktop.Customization defines CAIRO_FEATURES (see docs/features/README.md). Never compiled on Linux.
#if CAIRO_FEATURES
using System;
using System.Windows.Controls;
using System.Windows.Input;
using CairoDesktop.Features.FocusTimer;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.FocusTimer
{
    internal sealed class FocusTimerWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "focus";

        public ICairoWidget Create(WidgetContext context) => new FocusTimerWidget(context);
    }

    /// <summary>
    /// Pomodoro timer text on a bar. Left click starts/pauses, right click opens Skip / Reset.
    /// Options (all optional): workMinutes, shortBreakMinutes, longBreakMinutes, sessionsBeforeLongBreak, autoStart, sound.
    /// </summary>
    internal sealed class FocusTimerWidget : TextWidget
    {
        private readonly FocusTimerModel _model;
        private readonly bool _sound;

        public FocusTimerWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(1))
        {
            var options = new FocusTimerOptions
            {
                WorkMinutes = ReadInt(context, "workMinutes", 25),
                ShortBreakMinutes = ReadInt(context, "shortBreakMinutes", 5),
                LongBreakMinutes = ReadInt(context, "longBreakMinutes", 15),
                SessionsBeforeLongBreak = ReadInt(context, "sessionsBeforeLongBreak", 4),
                AutoStartNext = context.Options.GetBool("autoStart", false),
            };
            foreach (string finding in options.Normalize()) context.Log(finding);
            _sound = context.Options.GetBool("sound", true);
            _model = new FocusTimerModel(options);

            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnLeftClick;
            TextBlock.MouseRightButtonUp += OnRightClick;
        }

        private static int ReadInt(WidgetContext c, string name, int fallback)
        {
            double d = c.Options.GetNumber(name, fallback);
            return (int)Math.Max(-1000000, Math.Min(1000000, Math.Round(d))); // Normalize() clamps into the real range
        }

        protected override string GetText()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            FocusTransition t = _model.Tick(now);
            if (t != null && _sound)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { /* no audio device: ignore */ }
            }
            return _model.FormatText(now);
        }

        protected override string GetToolTip() => _model.FormatToolTip(DateTimeOffset.UtcNow);

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            _model.Toggle(DateTimeOffset.UtcNow);
            Refresh();
            e.Handled = true;
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            var skip = new MenuItem { Header = "Skip to next phase" };
            skip.Click += (s, a) => { _model.Skip(); Refresh(); };
            var reset = new MenuItem { Header = "Reset" };
            reset.Click += (s, a) => { _model.Reset(); Refresh(); };
            menu.Items.Add(skip);
            menu.Items.Add(reset);
            menu.IsOpen = true;
            e.Handled = true;
        }

        public override void Dispose()
        {
            TextBlock.MouseLeftButtonUp -= OnLeftClick;
            TextBlock.MouseRightButtonUp -= OnRightClick;
            base.Dispose();
        }
    }
}
#endif
