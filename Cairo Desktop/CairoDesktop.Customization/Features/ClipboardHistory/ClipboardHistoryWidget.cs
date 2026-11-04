// Inert until CairoDesktop.Customization references CairoDesktop.Features and defines CAIRO_FEATURES
// (see docs/features/clipboard-history.md, "Wiring status"). Guarded so an unwired checkout still builds.
#if CAIRO_FEATURES
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using CairoDesktop.Widgets.Sdk;
using CairoDesktop.Features.ClipboardHistory;

namespace CairoDesktop.Customization.Features.ClipboardHistory
{
    /// <summary>
    /// Shared per process: every bar/monitor gets its own widget, but there must be exactly one clipboard listener and one store.
    /// Reference counted; the last widget to be disposed tears the monitor down. UI thread only.
    /// </summary>
    internal sealed class ClipboardHistoryService
    {
        private static ClipboardHistoryService _instance;
        private static int _refs;

        private readonly Dispatcher _dispatcher;
        private readonly SensitivePolicy _policy;
        private readonly ClipboardMonitor _monitor;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _expireTimer;
        private readonly string _file = Path.Combine(CairoPlusPaths.DataFolder, "clipboard-history.json");

        private ClipboardHistoryService(WidgetContext c)
        {
            _dispatcher = c.Dispatcher;
            Config = new ClipboardConfig
            {
                Enabled = c.Options.GetBool("enabled", true),     // placing the widget is the opt-in
                MaxItems = (int)Math.Max(1, Math.Min(1000, c.Options.GetNumber("maxItems", 100))),
                TtlMinutes = (int)Math.Max(0, Math.Min(525600, c.Options.GetNumber("ttlMinutes", 1440))), // clamp: an out-of-range double cast to int wraps negative, which would mean "never expire"
                Persist = c.Options.GetBool("persist", false),
                PinnedMax = (int)Math.Max(0, Math.Min(200, c.Options.GetNumber("pinnedMax", 20))),
                PreviewLength = (int)Math.Max(10, Math.Min(500, c.Options.GetNumber("previewLength", 80))),
            };
            if (c.Options.Has("excludeApps"))
                Config.ExcludeApps = c.Options.GetString("excludeApps", "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

            _policy = new SensitivePolicy(Config.ToPolicyOptions());
            Store = new ClipboardHistoryStore(Config.ToStoreOptions());

            if (Config.Persist) LoadFromDisk(c);
            Store.Changed += OnStoreChanged;

            _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(3) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); if (!_released) SaveToDisk(c); };

            // The store only expires on writes; without this, clips older than the TTL stay visible (and get saved) while idle.
            _expireTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMinutes(1) };
            _expireTimer.Tick += (s, e) => Store.Expire();
            _expireTimer.Start();

            _monitor = new ClipboardMonitor(_dispatcher);
            _monitor.TextCaptured += OnCaptured;
            if (Config.Enabled)
            {
                try { _monitor.Start(); }
                catch (Exception ex) { c.Log("clipboard monitor failed to start: " + ex.Message); }
            }
        }

        public ClipboardConfig Config { get; }
        public ClipboardHistoryStore Store { get; }

        /// <summary>Raised on the UI thread, coalesced, after the store changed.</summary>
        public event Action Changed;

        public static ClipboardHistoryService Acquire(WidgetContext c)
        {
            if (_instance == null) _instance = new ClipboardHistoryService(c);
            _refs++;
            return _instance;
        }

        public void Release(WidgetContext c)
        {
            if (_released || --_refs > 0) return;
            _instance = null;
            _released = true;
            Store.Changed -= OnStoreChanged;
            _monitor.TextCaptured -= OnCaptured;
            _monitor.Dispose();
            _saveTimer.Stop();
            _expireTimer.Stop();
            if (Config.Persist) SaveToDisk(c);
        }

        public bool CopyBack(ClipboardEntry entry) => _monitor.SetText(entry.Text);

        private void OnCaptured(string text, string app, ClipboardFormatFlags flags)
        {
            try
            {
                if (_policy.Evaluate(text, app, flags).Allowed) Store.Add(text, app);
            }
            catch (Exception)
            {
                // Runs inside a DispatcherTimer tick: an exception here would take the shell down. Drop the clip instead
                // (never log it: its content must not reach log files).
            }
            // A refused clip is dropped silently and never logged: its content must not leak into log files either.
        }

        private int _notifyQueued; // Interlocked: the store may raise Changed from any thread
        private volatile bool _released;

        private void OnStoreChanged(object sender, HistoryChangedEventArgs e)
        {
            // Raised outside the store's lock, possibly on any thread: hop to the UI and coalesce bursts.
            if (_released || _dispatcher.HasShutdownStarted) return;
            if (System.Threading.Interlocked.Exchange(ref _notifyQueued, 1) == 1) return;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                System.Threading.Interlocked.Exchange(ref _notifyQueued, 0);
                if (_released) return;
                Changed?.Invoke();
                if (Config.Persist) { _saveTimer.Stop(); _saveTimer.Start(); }
            }));
        }

        private void LoadFromDisk(WidgetContext c)
        {
            try
            {
                if (!File.Exists(_file)) return;
                LoadedHistory loaded = HistoryPersistence.Deserialize(File.ReadAllText(_file), _policy);
                foreach (string finding in loaded.Findings) c.Log(finding);
                Store.Load(loaded.Entries);
            }
            catch (Exception ex) { c.Log("could not read clipboard history: " + ex.Message); }
        }

        private void SaveToDisk(WidgetContext c)
        {
            try
            {
                Store.Expire(); // never write clips that are already past their TTL
                string json = HistoryPersistence.Serialize(Store.Snapshot(), Config.Persist, _policy);
                if (json == null) return;
                string error = AtomicFile.WriteToDisk(_file, json);
                if (error != null) c.Log(error);
            }
            catch (Exception ex)
            {
                c.Log("could not save clipboard history: " + ex.Message); // runs from a timer tick and from Dispose: must not throw
            }
        }
    }

    internal sealed class ClipboardHistoryWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "clipboard";

        public ICairoWidget Create(WidgetContext context) => new ClipboardHistoryWidget(context);
    }

    internal sealed class ClipboardHistoryWidget : ICairoWidget
    {
        private sealed class Row
        {
            public ClipboardEntry Entry { get; set; }
            public string Preview { get; set; }
            public string PinGlyph { get; set; }
        }

        private readonly WidgetContext _context;
        private readonly ClipboardHistoryService _service;
        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private readonly Button _button = new Button { Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
        private readonly Popup _popup = new Popup { StaysOpen = false, AllowsTransparency = true };
        private readonly TextBox _search = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        private readonly ListBox _list = new ListBox { MaxHeight = 360, MinWidth = 320, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly Button _clear = new Button { Content = "Clear unpinned", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        private bool _disposed;

        public ClipboardHistoryWidget(WidgetContext context)
        {
            _context = context;
            _service = ClipboardHistoryService.Acquire(context);
            try
            {
                Initialize(context);
            }
            catch
            {
                _service.Release(context); // a half-built widget is never disposed by the host
                throw;
            }
        }

        private void Initialize(WidgetContext context)
        {

            var label = new TextBlock { Text = context.Options.GetString("label", "📋") };
            WidgetTheme.ApplyText(label, context.Bar);
            _button.Content = label;
            _button.ToolTip = "Clipboard history";

            _clear.Click += OnClear;
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(_search);
            panel.Children.Add(_list);
            panel.Children.Add(_clear);
            var frame = new Border { Child = panel, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            frame.SetResourceReference(Border.BackgroundProperty, WidgetTheme.HoverBrushKey);
            frame.SetResourceReference(Border.BorderBrushProperty, WidgetTheme.TrackBrushKey);
            _popup.Child = frame;
            _popup.PlacementTarget = _button;
            _popup.Placement = context.Bar == WidgetBar.Taskbar ? PlacementMode.Top : PlacementMode.Bottom;

            _list.ItemsSource = _rows;
            _list.ItemTemplate = BuildRowTemplate();
            _list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowClick));
            _button.Click += OnButtonClick;
            _search.TextChanged += OnSearchChanged;
            _service.Changed += Refresh;
            Refresh();
        }

        public FrameworkElement View => _button;

        private static DataTemplate BuildRowTemplate()
        {
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            AddRowButton(dock, "delete", "✕", "Delete");
            AddRowButton(dock, "copy", "⎘", "Copy back to clipboard");
            var pin = AddRowButton(dock, "pin", null, "Pin or unpin");
            pin.SetBinding(ContentControl.ContentProperty, new Binding("PinGlyph"));
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Preview"));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dock.AppendChild(text);
            return new DataTemplate { VisualTree = dock };
        }

        private static FrameworkElementFactory AddRowButton(FrameworkElementFactory parent, string tag, string glyph, string tip)
        {
            var b = new FrameworkElementFactory(typeof(Button));
            if (glyph != null) b.SetValue(ContentControl.ContentProperty, glyph);
            b.SetValue(FrameworkElement.TagProperty, tag);
            b.SetValue(FrameworkElement.ToolTipProperty, tip);
            b.SetValue(DockPanel.DockProperty, Dock.Right);
            b.SetValue(Control.PaddingProperty, new Thickness(4, 0, 4, 0));
            b.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
            parent.AppendChild(b);
            return b;
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            _popup.IsOpen = !_popup.IsOpen;
            if (_popup.IsOpen) { _service.Store.Expire(); _search.Text = ""; Refresh(); }
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void OnClear(object sender, RoutedEventArgs e) => _service.Store.Clear(true);

        private void OnRowClick(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            var row = b?.DataContext as Row;
            if (row == null) return;
            e.Handled = true;
            switch (b.Tag as string)
            {
                case "pin": _service.Store.SetPinned(row.Entry.Id, !row.Entry.Pinned); break;
                case "delete": _service.Store.Remove(row.Entry.Id); break;
                case "copy":
                    if (_service.CopyBack(row.Entry)) { _service.Store.Add(row.Entry.Text); _popup.IsOpen = false; }
                    break;
            }
        }

        private void Refresh()
        {
            if (_disposed) return;
            int preview = _service.Config.PreviewLength;
            List<Row> fresh = _service.Store.Search(_search.Text, null).Select(en => new Row
            {
                Entry = en,
                Preview = en.Preview(preview),
                PinGlyph = en.Pinned ? "📌" : "○"
            }).ToList();
            _rows.Clear();
            foreach (Row r in fresh) _rows.Add(r);
            _button.ToolTip = "Clipboard history (" + _service.Store.Count + ")";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _popup.IsOpen = false;
            _popup.Child = null;
            _service.Changed -= Refresh;
            _button.Click -= OnButtonClick;
            _clear.Click -= OnClear;
            _search.TextChanged -= OnSearchChanged;
            _list.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowClick));
            _list.ItemsSource = null;
            _rows.Clear();
            _service.Release(_context);
        }
    }
}
#endif
