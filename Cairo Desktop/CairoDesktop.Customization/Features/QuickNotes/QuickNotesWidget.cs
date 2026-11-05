// Compiled only with -p:CairoExtras=true (see docs/features/WINDOWS-VERIFY.md).
#if CAIRO_FEATURES || CAIRO_EXTRAS
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CairoDesktop.Features.QuickNotes;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Features.QuickNotes
{
    internal sealed class QuickNotesWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "quicknotes";

        public ICairoWidget Create(WidgetContext context) => new QuickNotesWidget(context);
    }

    /// <summary>One notes list for every quicknotes widget (one per bar), saved to %LOCALAPPDATA%\Cairo Desktop\quick-notes.txt. UI thread only.</summary>
    internal sealed class QuickNotesStore
    {
        private static QuickNotesStore _instance;
        private readonly string _file = Path.Combine(CairoPlusPaths.DataFolder, "quick-notes.txt");

        private QuickNotesStore(QuickNotesOptions options, Action<string> log)
        {
            Model = new QuickNotesModel(options);
            try
            {
                if (File.Exists(_file))
                {
                    int skipped = Model.Load(File.ReadAllText(_file));
                    if (skipped > 0) log("quick-notes.txt: " + skipped + " unreadable line(s) were skipped.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log("could not read quick-notes.txt: " + ex.Message);
            }
        }

        public QuickNotesModel Model { get; }

        /// <summary>The first widget's options win; later widgets share the same list.</summary>
        public static QuickNotesStore Get(QuickNotesOptions options, Action<string> log)
        {
            if (_instance == null) _instance = new QuickNotesStore(options, log);
            return _instance;
        }

        public void Save(Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllText(_file, Model.Serialize());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log("could not save quick-notes.txt: " + ex.Message);
            }
        }
    }

    /// <summary>Click to type a note (Enter saves, Esc cancels). Right click: copy or delete a note, clear all. Options: label, maxNotes, maxLength, toolTipNotes.</summary>
    internal sealed class QuickNotesWidget : TextWidget
    {
        private readonly QuickNotesStore _store;

        public QuickNotesWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(5))
        {
            var options = new QuickNotesOptions
            {
                Label = context.Options.GetString("label", "Notes"),
                MaxNotes = (int)context.Options.GetNumber("maxNotes", 50),
                MaxLength = (int)context.Options.GetNumber("maxLength", 500),
                ToolTipNotes = (int)context.Options.GetNumber("toolTipNotes", 8),
            };
            foreach (string finding in options.Normalize()) context.Log(finding);
            _store = QuickNotesStore.Get(options, context.Log);
            TextBlock.Cursor = Cursors.Hand;
            TextBlock.MouseLeftButtonUp += OnLeftClick;
            TextBlock.MouseRightButtonUp += OnRightClick;
        }

        protected override string GetText() => _store.Model.FormatText();

        protected override string GetToolTip() => _store.Model.FormatToolTip();

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            var box = new TextBox { Width = 280, MaxLength = 5000 };
            var popup = new Popup
            {
                PlacementTarget = TextBlock,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                Child = new Border
                {
                    Padding = new Thickness(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = SystemColors.ActiveBorderBrush,
                    Background = SystemColors.WindowBrush,
                    Child = box,
                },
            };
            box.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    if (_store.Model.Add(box.Text, DateTimeOffset.Now)) _store.Save(Context.Log);
                    popup.IsOpen = false;
                    Refresh();
                    args.Handled = true;
                }
                else if (args.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                    args.Handled = true;
                }
            };
            popup.Opened += (s, args) => box.Focus();
            popup.IsOpen = true;
            e.Handled = true;
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            QuickNotesModel model = _store.Model;
            for (int i = model.Count - 1; i >= 0 && menu.Items.Count < 10; i--)
            {
                int index = i;
                string text = model.Notes[i].Text;
                string header = text.Length > 40 ? text.Substring(0, 39) + "…" : text;
                var item = new MenuItem { Header = header, ToolTip = "Click to copy. Use 'Delete' below to remove." };
                item.Click += (s, a) =>
                {
                    try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
                };
                var delete = new MenuItem { Header = "Delete" };
                delete.Click += (s, a) => { if (model.RemoveAt(index)) { _store.Save(Context.Log); Refresh(); } };
                item.Items.Add(delete);
                menu.Items.Add(item);
            }

            if (model.Count == 0) menu.Items.Add(new MenuItem { Header = "No notes", IsEnabled = false });
            else
            {
                menu.Items.Add(new Separator());
                var clear = new MenuItem { Header = "Clear all" };
                clear.Click += (s, a) => { model.Clear(); _store.Save(Context.Log); Refresh(); };
                menu.Items.Add(clear);
            }

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
