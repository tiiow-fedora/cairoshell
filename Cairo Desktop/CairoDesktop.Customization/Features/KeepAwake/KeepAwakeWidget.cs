// Inert until CairoDesktop.Customization defines CAIRO_FEATURES (see docs/features/README.md). Never compiled on Linux.
#if CAIRO_FEATURES
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CairoDesktop.Features.KeepAwake;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.KeepAwake
{
    internal sealed class KeepAwakeWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "keepawake";

        public ICairoWidget Create(WidgetContext context) => new KeepAwakeWidget(context);
    }

    /// <summary>
    /// One state shared by every keepawake widget (a bar per monitor each get their own widget, but there is one Windows
    /// "stay awake" request). Reference counted: the last widget to go away releases the request. UI thread only.
    /// </summary>
    internal sealed class KeepAwakeService
    {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        private static KeepAwakeService _instance;
        private static int _refs;

        private bool _applied;

        private KeepAwakeService(KeepAwakeOptions options)
        {
            Model = new KeepAwakeModel(options);
        }

        public KeepAwakeModel Model { get; }

        public static KeepAwakeService Acquire(KeepAwakeOptions options)
        {
            if (_instance == null) _instance = new KeepAwakeService(options);
            _refs++;
            return _instance;
        }

        public void Release()
        {
            if (--_refs > 0) return;
            _refs = 0;
            _instance = null;
            Model.TurnOff();
            Sync(DateTimeOffset.Now);
        }

        /// <summary>Makes Windows match the model. The execution state belongs to the calling thread, so always call on the UI thread.</summary>
        public void Sync(DateTimeOffset now)
        {
            bool active = Model.IsActive(now);
            if (active)
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | (Model.KeepDisplayOn ? ES_DISPLAY_REQUIRED : 0));
                _applied = true;
            }
            else if (_applied)
            {
                SetThreadExecutionState(ES_CONTINUOUS);
                _applied = false;
            }
        }
    }

    /// <summary>
    /// Click to cycle off, 30 min, 1 h, 2 h, until off. Options: presetMinutes ("30,60,120"), keepDisplayOn, label.
    /// </summary>
    internal sealed class KeepAwakeWidget : TextWidget
    {
        private readonly KeepAwakeService _service;

        public KeepAwakeWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(30))
        {
            var options = new KeepAwakeOptions
            {
                KeepDisplayOn = context.Options.GetBool("keepDisplayOn", false),
                Label = context.Options.GetString("label", "Awake"),
            };
            string presets = context.Options.GetString("presetMinutes", null);
            if (!string.IsNullOrWhiteSpace(presets))
            {
                var list = new List<int>();
                foreach (string part in presets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int minutes)) list.Add(minutes);
                    else context.Log("presetMinutes: '" + part.Trim() + "' is not a number and was ignored.");
                }

                options.PresetMinutes = list;
            }

            foreach (string finding in options.Normalize()) context.Log(finding);
            _service = KeepAwakeService.Acquire(options);
            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnClick;
        }

        protected override string GetText()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            _service.Model.Tick(now);
            _service.Sync(now);
            return _service.Model.FormatText(now);
        }

        protected override string GetToolTip() => _service.Model.FormatToolTip(DateTimeOffset.Now);

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            _service.Model.Cycle(DateTimeOffset.Now);
            Refresh();
            e.Handled = true;
        }

        public override void Dispose()
        {
            TextBlock.MouseLeftButtonUp -= OnClick;
            base.Dispose();
            _service.Release();
        }
    }
}
#endif
