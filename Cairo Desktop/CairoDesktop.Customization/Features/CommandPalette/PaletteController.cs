#if CAIRO_FEATURES
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CairoDesktop.Features.CommandPalette;

namespace CairoDesktop.Customization.Features.CommandPalette
{
    /// <summary>
    /// Owns the palette window: Toggle() opens or closes it, typing is debounced, Enter runs the selected item.
    /// All members must be called on the UI thread (the Dispatcher passed to the constructor).
    ///
    /// Traps handled:
    ///  - The debounce DispatcherTimer is stopped and its Tick detached in Dispose, otherwise it keeps the controller alive.
    ///  - The window is Closed (not Hidden) in Dispose and on every dismissal; the next Toggle builds a fresh one,
    ///    so there is never a hidden window holding handlers. No static field refers to it.
    ///  - Providers run synchronously: they are cheap by contract. If a host provider ever needs background work,
    ///    marshal its results back with Dispatcher.BeginInvoke and drop them unless <c>generation</c> still matches
    ///    and the controller is not disposed (the same check <see cref="Search"/> already does).
    ///  - Process.Start with UseShellExecute is used only for http/https URLs and paths that exist.
    /// </summary>
    internal sealed class PaletteController : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly PaletteEngine _engine;
        private readonly int _maxResults;
        private readonly Action<string> _log;
        private readonly DispatcherTimer _debounce;
        private PaletteWindow _window;
        private string _pendingText = "";
        private int _generation;
        private bool _disposed;

        public PaletteController(Dispatcher dispatcher, PaletteEngine engine, int maxResults, Action<string> log)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _maxResults = maxResults;
            _log = log ?? (_ => { });

            _debounce = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = TimeSpan.FromMilliseconds(60) };
            _debounce.Tick += OnDebounceTick;
        }

        public void Toggle()
        {
            if (_disposed) return;
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(Toggle));
                return;
            }

            if (_window != null)
            {
                _window.CloseOnce();
                return;
            }

            var w = new PaletteWindow();
            w.QueryChanged += OnQueryChanged;
            w.Accepted += OnAccepted;
            w.Closed += OnWindowClosed;
            _window = w;
            try
            {
                w.Show();
            }
            catch (Exception ex)
            {
                _log("command palette: could not show window: " + ex.Message);
                w.QueryChanged -= OnQueryChanged;
                w.Accepted -= OnAccepted;
                w.Closed -= OnWindowClosed;
                _window = null;
                return;
            }

            Search("");
        }

        private void OnQueryChanged(string text)
        {
            _pendingText = text ?? "";
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounce.Stop();
            Search(_pendingText);
        }

        private void Search(string text)
        {
            int generation = ++_generation;
            PaletteSearchResult result;
            try
            {
                result = _engine.Search(text, _maxResults);
            }
            catch (Exception ex)
            {
                // Never let a search failure escape into the hotkey/timer callback (it would crash the shell).
                _log("command palette: search failed: " + ex.Message);
                return;
            }

            // Stale or late results are dropped: the window may have closed or a newer query started.
            if (_disposed || _window == null || generation != _generation) return;

            foreach (string d in result.Diagnostics) _log("command palette: " + d);
            _window.SetResults(result.Results);
        }

        private void OnAccepted(PaletteResult result)
        {
            var item = result.Item;
            _engine.RecordLaunch(item);
            _window?.CloseOnce(); // close first so focus returns to the previous window before the launch
            Execute(item.Payload);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            var w = (PaletteWindow)sender;
            w.QueryChanged -= OnQueryChanged;
            w.Accepted -= OnAccepted;
            w.Closed -= OnWindowClosed;
            _debounce.Stop();
            _generation++; // invalidate anything in flight
            if (ReferenceEquals(_window, w)) _window = null;
        }

        private void Execute(string payload)
        {
            if (!PaletteAction.TryParse(payload, out PaletteAction action)) return; // non-actionable item

            try
            {
                switch (action.Kind)
                {
                    case PaletteActionKind.Copy:
                        System.Windows.Clipboard.SetText(action.Target);
                        break;

                    case PaletteActionKind.Url:
                        if (!PaletteAction.IsSafeWebUrl(action.Target)) { _log("command palette: refused to open non-http(s) URL."); return; }
                        Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });
                        break;

                    case PaletteActionKind.Launch:
                        string target = Environment.ExpandEnvironmentVariables(action.Target.Trim().Trim('"'));
                        string arguments = Environment.ExpandEnvironmentVariables(action.Arguments ?? "");
                        bool isPath = File.Exists(target) || Directory.Exists(target);
                        // Existing files/folders may use the shell (open with the default app). A bare command name
                        // is started directly instead, so arbitrary text can never be handed to the shell verbs.
                        var psi = new ProcessStartInfo(target, arguments) { UseShellExecute = isPath };
                        Process.Start(psi);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log("command palette: could not run item: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!_dispatcher.CheckAccess())
            {
                // DispatcherTimer and Window are thread-affine: do all of the teardown on the UI thread.
                _dispatcher.BeginInvoke(new Action(Dispose));
                return;
            }

            _disposed = true;
            _generation++;

            _debounce.Stop();
            _debounce.Tick -= OnDebounceTick;

            var w = _window;
            w?.CloseOnce(); // OnWindowClosed detaches the handlers
        }
    }
}
#endif
