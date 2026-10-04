using System;
using System.Windows;

namespace CairoDesktop.Widgets.Sdk
{
    /// <summary>
    /// Creates widgets of one type. Cairo Plus finds every public, non-abstract class implementing this
    /// interface (with a parameterless constructor) in the DLLs placed in a Widgets folder.
    /// </summary>
    public interface ICairoWidgetFactory
    {
        /// <summary>
        /// The "type" used in cairo-plus.json, e.g. "uptime". Lowercase, no spaces.
        /// Built-in types (clock, cpu, memory, battery, network, command) can't be replaced.
        /// </summary>
        string Type { get; }

        /// <summary>Creates one widget instance. Called on the UI thread, once per bar (and per monitor).</summary>
        ICairoWidget Create(WidgetContext context);
    }

    /// <summary>
    /// One widget instance placed on a bar. Disposed when the bar is rebuilt (config reload) or closed,
    /// so stop timers and release resources in <see cref="IDisposable.Dispose"/>.
    /// </summary>
    public interface ICairoWidget : IDisposable
    {
        /// <summary>The element shown on the bar. Created once; update its contents rather than replacing it.</summary>
        FrameworkElement View { get; }
    }

    /// <summary>Which bar a widget is placed on.</summary>
    public enum WidgetBar
    {
        /// <summary>The menu bar (top of the screen by default).</summary>
        MenuBar,

        /// <summary>The taskbar (bottom of the screen by default).</summary>
        Taskbar
    }
}
