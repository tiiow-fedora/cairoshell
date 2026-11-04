// Compiled only with -p:CairoExtras=true (see docs/features/WINDOWS-VERIFY.md).
#if CAIRO_FEATURES || CAIRO_EXTRAS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using CairoDesktop.Features.PowerPlan;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.PowerPlan
{
    internal sealed class PowerPlanWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "powerplan";

        public ICairoWidget Create(WidgetContext context) => new PowerPlanWidget(context);
    }

    /// <summary>
    /// Shows the active Windows power plan; click switches to the next one. Options: label, cycle ("Balanced;High performance"; names or GUIDs),
    /// interval (seconds between checks, default 30). Runs powercfg.exe off the UI thread.
    /// </summary>
    internal sealed class PowerPlanWidget : TextWidget
    {
        private readonly PowerPlanModel _model;
        private IList<PowerPlanInfo> _plans = new List<PowerPlanInfo>();
        private int _busy; // 1 while a powercfg call is running
        private bool _disposed;
        private bool _reportedFailure;

        public PowerPlanWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(Math.Max(5, Math.Min(600, context.Options.GetNumber("interval", 30)))))
        {
            var options = new PowerPlanOptions { Label = context.Options.GetString("label", "Power") };
            string cycle = context.Options.GetString("cycle", null);
            if (!string.IsNullOrWhiteSpace(cycle))
            {
                var list = new List<string>();
                foreach (string part in cycle.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (part.Trim().Length > 0) list.Add(part.Trim());
                options.Cycle = list;
            }

            _model = new PowerPlanModel(options);
            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnClick;
            Reload(null);
        }

        protected override string GetText()
        {
            Reload(null); // asynchronous; shows the last known state until it finishes
            return _model.FormatText(_plans);
        }

        protected override string GetToolTip() => _model.FormatToolTip(_plans);

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            PowerPlanInfo next = _model.Next(_plans);
            if (next != null) Reload(next.Guid);
            e.Handled = true;
        }

        /// <summary>Optionally activates <paramref name="activateGuid"/>, then re-reads the plan list. One call at a time.</summary>
        private void Reload(string activateGuid)
        {
            if (_disposed || System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            Task.Run(() =>
            {
                string output = null;
                string error = null;
                try
                {
                    if (activateGuid != null) Run("/setactive " + activateGuid);
                    output = Run("/list");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Context.Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _busy, 0);
                    if (_disposed) return;
                    if (error != null)
                    {
                        if (!_reportedFailure) { _reportedFailure = true; Context.Log("powercfg failed: " + error); }
                        return;
                    }

                    _plans = PowerPlanParser.Parse(output);
                    TextBlock.Text = _model.FormatText(_plans);
                    TextBlock.ToolTip = _model.FormatToolTip(_plans);
                }));
            });
        }

        private static string Run(string arguments)
        {
            var info = new ProcessStartInfo("powercfg.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using (Process process = Process.Start(info))
            {
                string text = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("powercfg did not finish in 5 seconds.");
                }

                return text;
            }
        }

        public override void Dispose()
        {
            _disposed = true;
            TextBlock.MouseLeftButtonUp -= OnClick;
            base.Dispose();
        }
    }
}
#endif
