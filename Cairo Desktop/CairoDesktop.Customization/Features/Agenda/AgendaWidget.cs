// Compiled only with -p:CairoExtras=true (see docs/features/WINDOWS-VERIFY.md).
#if CAIRO_FEATURES || CAIRO_EXTRAS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using CairoDesktop.Features.Agenda;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.Agenda
{
    internal sealed class AgendaWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "agenda";

        public ICairoWidget Create(WidgetContext context) => new AgendaWidget(context);
    }

    /// <summary>
    /// Next calendar event from an .ics file (export or sync one from your calendar app). The file is re-read when it changes.
    /// Options: file (required), horizonDays, maxItems, maxTextLength, timeFormat, label, interval (seconds, default 30).
    /// Click re-reads the file now; right click opens it.
    /// </summary>
    internal sealed class AgendaWidget : TextWidget
    {
        private readonly string _file;
        private readonly AgendaOptions _options;
        private AgendaPlanner _planner;
        private DateTime _loadedStamp = DateTime.MinValue;
        private bool _reportedMissing;

        public AgendaWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(Math.Max(5, Math.Min(600, context.Options.GetNumber("interval", 30)))))
        {
            _file = Environment.ExpandEnvironmentVariables(context.Options.GetString("file", "") ?? "");
            _options = new AgendaOptions
            {
                HorizonDays = (int)context.Options.GetNumber("horizonDays", 7),
                MaxItems = (int)context.Options.GetNumber("maxItems", 12),
                MaxTextLength = (int)context.Options.GetNumber("maxTextLength", 40),
                TimeFormat = context.Options.GetString("timeFormat", "HH:mm"),
                Label = context.Options.GetString("label", "Agenda"),
            };
            foreach (string finding in _options.Normalize()) context.Log(finding);
            if (string.IsNullOrWhiteSpace(_file)) context.Log("option 'file' (path to an .ics file) is not set.");
            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnLeftClick;
            TextBlock.MouseRightButtonUp += OnRightClick;
        }

        private void LoadIfChanged(bool force)
        {
            if (string.IsNullOrWhiteSpace(_file)) return;
            if (!File.Exists(_file))
            {
                _planner = null;
                if (!_reportedMissing) { _reportedMissing = true; Context.Log("calendar file not found: " + _file); }
                return;
            }

            _reportedMissing = false;
            DateTime stamp = File.GetLastWriteTimeUtc(_file);
            if (!force && _planner != null && stamp == _loadedStamp) return;
            try
            {
                IcsResult result = IcsParser.Parse(File.ReadAllText(_file), TimeZoneInfo.Local, FindZone);
                foreach (string problem in result.Problems) Context.Log(problem);
                _planner = new AgendaPlanner(new List<IcsEvent>(result.Events), TimeZoneInfo.Local, _options, CultureInfo.CurrentCulture);
                _loadedStamp = stamp;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Context.Log("could not read the calendar file: " + ex.Message); // keep the last good data
            }
        }

        private static TimeZoneInfo FindZone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

        protected override string GetText()
        {
            LoadIfChanged(false);
            if (_planner == null) return string.IsNullOrWhiteSpace(_file) ? _options.Label + ": no file" : _options.Label + ": ?";
            return _planner.FormatText(DateTimeOffset.Now);
        }

        protected override string GetToolTip() => _planner?.FormatToolTip(DateTimeOffset.Now);

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            LoadIfChanged(true);
            Refresh();
            e.Handled = true;
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open calendar file", IsEnabled = !string.IsNullOrWhiteSpace(_file) && File.Exists(_file) };
            open.Click += (s, a) =>
            {
                try { Process.Start(new ProcessStartInfo(_file) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { Context.Log("could not open the file: " + ex.Message); }
            };
            menu.Items.Add(open);
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
