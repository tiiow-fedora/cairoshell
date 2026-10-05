using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Bars
{
    /// <summary>
    /// What a Cairo bar window hands to Cairo Plus so its items can be rearranged without
    /// Cairo Plus knowing the bar's concrete type. Created by the hook in MenuBar/Taskbar.
    /// </summary>
    public sealed class BarHost
    {
        /// <param name="window">The bar window.</param>
        /// <param name="kind">Menu bar or taskbar.</param>
        /// <param name="container">The panel whose children make up the bar's normal layout.</param>
        /// <param name="getItems">Returns the bar's built-in items by id, in their default order.</param>
        /// <param name="layoutChanged">Called after items move or change size (the taskbar recomputes button widths).</param>
        /// <param name="surface">The element that paints the bar's background (gets margins, rounded corners). Defaults to the container.</param>
        /// <param name="refreshThickness">
        /// Re-applies the bar's reserved height; the bar adds <see cref="CairoPlusHooks.GetExtraThickness"/> to its normal height.
        /// </param>
        public BarHost(Window window, WidgetBar kind, Panel container,
            Func<IList<KeyValuePair<string, FrameworkElement>>> getItems,
            Action layoutChanged,
            FrameworkElement surface = null,
            Action refreshThickness = null)
        {
            Window = window ?? throw new ArgumentNullException(nameof(window));
            Kind = kind;
            Container = container ?? throw new ArgumentNullException(nameof(container));
            GetItems = getItems ?? throw new ArgumentNullException(nameof(getItems));
            LayoutChanged = layoutChanged;
            Surface = surface ?? container;
            RefreshThickness = refreshThickness;
        }

        public Window Window { get; }

        public WidgetBar Kind { get; }

        public Panel Container { get; }

        public Func<IList<KeyValuePair<string, FrameworkElement>>> GetItems { get; }

        public Action LayoutChanged { get; }

        public FrameworkElement Surface { get; }

        public Action RefreshThickness { get; }
    }
}
