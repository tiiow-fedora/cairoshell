using System;
using System.Windows.Threading;

namespace CairoDesktop.Widgets.Sdk
{
    /// <summary>Everything a widget gets from Cairo Plus when it is created.</summary>
    public sealed class WidgetContext
    {
        /// <summary>Creates a context. Cairo Plus does this; widgets only consume it (tests may construct one).</summary>
        public WidgetContext(string id, WidgetBar bar, IWidgetOptions options, Dispatcher dispatcher, Action<string> log)
        {
            Id = id;
            Bar = bar;
            Options = options;
            Dispatcher = dispatcher;
            _log = log;
        }

        private readonly Action<string> _log;

        /// <summary>The widget's id from cairo-plus.json (the part after "widget:").</summary>
        public string Id { get; }

        /// <summary>Which bar the widget sits on; use it to pick colours (see <see cref="WidgetTheme"/>).</summary>
        public WidgetBar Bar { get; }

        /// <summary>The widget's options object from cairo-plus.json or the theme pack.</summary>
        public IWidgetOptions Options { get; }

        /// <summary>The UI thread's dispatcher. Update the view only on this thread.</summary>
        public Dispatcher Dispatcher { get; }

        /// <summary>Writes a line to Cairo's log, prefixed with the widget id.</summary>
        public void Log(string message)
        {
            _log?.Invoke($"widget '{Id}': {message}");
        }
    }

    /// <summary>Read-only access to a widget's options. Getters return the default when the option is missing or the wrong type.</summary>
    public interface IWidgetOptions
    {
        /// <summary>True if the option exists.</summary>
        bool Has(string name);

        /// <summary>A string option.</summary>
        string GetString(string name, string defaultValue = null);

        /// <summary>A numeric option.</summary>
        double GetNumber(string name, double defaultValue = 0);

        /// <summary>A boolean option.</summary>
        bool GetBool(string name, bool defaultValue = false);
    }
}
