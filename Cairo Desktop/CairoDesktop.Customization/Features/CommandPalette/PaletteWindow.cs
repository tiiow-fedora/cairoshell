// This file (and PaletteController.cs) is NOT compiled yet: the whole body sits behind CAIRO_FEATURES because
// CairoDesktop.Customization does not reference CairoDesktop.Features. To enable, add to the csproj:
//   <ProjectReference Include="..\CairoDesktop.Features\CairoDesktop.Features.csproj" />
//   <DefineConstants>$(DefineConstants);CAIRO_FEATURES</DefineConstants>
// It was written without a Windows compiler, so expect to fix small typos on the first real build.
#if CAIRO_FEATURES
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CairoDesktop.Features.CommandPalette;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfStackPanel = System.Windows.Controls.StackPanel;

namespace CairoDesktop.Customization.Features.CommandPalette
{
    /// <summary>
    /// The palette popup: a borderless, topmost, code-only window with a text box and a result list.
    /// It owns no logic; it raises <see cref="QueryChanged"/> / <see cref="Accepted"/> and shows what it is given.
    /// WPF rules followed here: everything runs on the Dispatcher thread that created the window; every handler
    /// attached in the constructor is detached in OnClosed; no static references to the window exist.
    /// </summary>
    internal sealed class PaletteWindow : Window
    {
        private readonly WpfTextBox _input = new WpfTextBox { FontSize = 20, Padding = new Thickness(8), BorderThickness = new Thickness(0) };
        private readonly WpfListBox _list = new WpfListBox { BorderThickness = new Thickness(0), MaxHeight = 360 };
        private IReadOnlyList<PaletteResult> _results = Array.Empty<PaletteResult>();
        private bool _closeRequested;

        public event Action<string> QueryChanged;
        public event Action<PaletteResult> Accepted;

        public PaletteWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height * 0.2;

            var panel = new WpfStackPanel();
            panel.Children.Add(_input);
            panel.Children.Add(_list);
            Content = panel;

            _input.TextChanged += OnTextChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            _list.MouseDoubleClick += OnListDoubleClick;
            Deactivated += OnDeactivated;
            Loaded += OnLoaded;
        }

        /// <summary>Shows results with the first one selected. Call on the UI thread only.</summary>
        public void SetResults(IReadOnlyList<PaletteResult> results)
        {
            _results = results ?? Array.Empty<PaletteResult>();
            _list.Items.Clear();
            foreach (var r in _results)
            {
                // Plain text on purpose: no templates, so nothing to leak or fail to resolve.
                _list.Items.Add(string.IsNullOrEmpty(r.Item.Subtitle) ? r.Item.Title : r.Item.Title + "   -   " + r.Item.Subtitle);
            }
            if (_results.Count > 0) _list.SelectedIndex = 0;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Activate();
            _input.Focus();
        }

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            QueryChanged?.Invoke(_input.Text);
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CloseOnce();
                    break;
                case Key.Down:
                    e.Handled = true;
                    if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                    break;
                case Key.Up:
                    e.Handled = true;
                    if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                    break;
                case Key.Enter:
                    e.Handled = true;
                    AcceptSelected();
                    break;
            }
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelected();

        private void AcceptSelected()
        {
            int i = _list.SelectedIndex;
            if (!_closeRequested && i >= 0 && i < _results.Count) Accepted?.Invoke(_results[i]);
        }

        // Clicking elsewhere (or alt-tabbing) dismisses the palette like Spotlight does.
        // Closing a window itself deactivates it, and WPF throws if Close() is called again while a window is
        // closing, so the close is deferred to the dispatcher and goes through the idempotent CloseOnce.
        private void OnDeactivated(object sender, EventArgs e)
        {
            if (_closeRequested) return;
            Dispatcher.BeginInvoke(new Action(CloseOnce));
        }

        /// <summary>Closes the window at most once; safe to call from Deactivated, key handlers or a queued dispatcher call.</summary>
        public void CloseOnce()
        {
            if (_closeRequested) return;
            _closeRequested = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closeRequested = true; // any other close path (Alt+F4) also blocks the deferred Deactivated close
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _input.TextChanged -= OnTextChanged;
            PreviewKeyDown -= OnPreviewKeyDown;
            _list.MouseDoubleClick -= OnListDoubleClick;
            Deactivated -= OnDeactivated;
            Loaded -= OnLoaded;
            QueryChanged = null;
            Accepted = null;
            base.OnClosed(e);
        }
    }
}
#endif
