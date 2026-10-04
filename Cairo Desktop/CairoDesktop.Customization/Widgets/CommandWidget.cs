using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Widgets
{
    /// <summary>
    /// "command": runs a command line on an interval and shows the first line of its output
    /// (like Waybar's custom modules). If the output is a JSON object, its "text" and "tooltip"
    /// fields are used. Options:
    ///   "command"  (required) command line, run through cmd.exe without a window
    ///   "interval" seconds between runs (default 10; 0 = run once)
    ///   "timeout"  seconds before a run is abandoned (default 10)
    ///   "label"    text shown before the output
    /// </summary>
    internal sealed class CommandWidgetFactory : ICairoWidgetFactory
    {
        public string Type => "command";

        public ICairoWidget Create(WidgetContext context) => new CommandWidget(context);

        private sealed class CommandWidget : ICairoWidget
        {
            private readonly WidgetContext _context;
            private readonly TextBlock _text = new TextBlock();
            private readonly DispatcherTimer _timer;
            private readonly string _command;
            private readonly string _label;
            private readonly TimeSpan _timeout;
            private int _running;
            private volatile bool _disposed;

            public CommandWidget(WidgetContext context)
            {
                _context = context;
                WidgetTheme.ApplyText(_text, context.Bar);

                _command = context.Options.GetString("command");
                _label = context.Options.GetString("label", "");
                _timeout = TimeSpan.FromSeconds(Math.Max(1, context.Options.GetNumber("timeout", 10)));

                if (string.IsNullOrWhiteSpace(_command))
                {
                    _text.Text = "⚠ no command";
                    _text.ToolTip = "Set \"command\" for this widget in cairo-plus.json.";
                    return;
                }

                _text.Text = _label;
                double interval = context.Options.GetNumber("interval", 10);
                if (interval > 0)
                {
                    _timer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Max(1, interval)), DispatcherPriority.Background, (s, e) => Run(), context.Dispatcher);
                    _timer.Start();
                }

                Run();
            }

            public FrameworkElement View => _text;

            private void Run()
            {
                // Skip a tick rather than piling up runs if the command is slow.
                if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

                Task.Run(() =>
                {
                    string output, error = null;
                    try
                    {
                        output = Execute(_command, _timeout);
                    }
                    catch (Exception ex)
                    {
                        output = null;
                        error = ex.Message;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }

                    if (_disposed) return;
                    _context.Dispatcher.BeginInvoke(new Action(() => Show(output, error)));
                });
            }

            private void Show(string output, string error)
            {
                if (_disposed) return;

                if (error != null)
                {
                    _text.Text = (_label + " ⚠").Trim();
                    _text.ToolTip = error;
                    _context.Log(error);
                    return;
                }

                ParseOutput(output, out string text, out string toolTip);
                _text.Text = string.IsNullOrEmpty(_label) ? text : _label + " " + text;
                _text.ToolTip = toolTip;
                _text.Visibility = string.IsNullOrEmpty(_text.Text) ? Visibility.Collapsed : Visibility.Visible;
            }

            internal static void ParseOutput(string output, out string text, out string toolTip)
            {
                output = (output ?? "").Trim();
                toolTip = null;

                if (output.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(output))
                        {
                            var root = doc.RootElement;
                            text = root.TryGetProperty("text", out JsonElement t) ? t.ToString() : "";
                            toolTip = root.TryGetProperty("tooltip", out JsonElement tt) ? tt.ToString() : null;
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON after all; show it as text.
                    }
                }

                int newline = output.IndexOfAny(new[] { '\r', '\n' });
                text = newline >= 0 ? output.Substring(0, newline) : output;
                toolTip = newline >= 0 ? output : null;
            }

            private static string Execute(string command, TimeSpan timeout)
            {
                var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(psi))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException($"command took longer than {timeout.TotalSeconds:0}s");
                    }

                    string output = stdout.Result;
                    if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                    {
                        string err = stderr.Result.Trim();
                        throw new Exception($"exit code {process.ExitCode}{(err.Length > 0 ? ": " + err : "")}");
                    }

                    return output;
                }
            }

            public void Dispose()
            {
                _disposed = true;
                _timer?.Stop();
            }
        }
    }
}
