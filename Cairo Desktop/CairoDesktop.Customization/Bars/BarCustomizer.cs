using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CairoDesktop.Customization.Actions;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Widgets;
using CairoDesktop.Widgets.Sdk;

namespace CairoDesktop.Customization.Bars
{
    /// <summary>
    /// Rearranges one bar window into left / center / right zones and hosts widgets in it.
    /// Everything it does can be undone exactly: <see cref="Restore"/> puts every element back
    /// into its original parent at its original position.
    /// </summary>
    internal sealed class BarCustomizer
    {
        public const string WidgetPrefix = "widget:";

        /// <summary>Items that stretch to fill the remaining space when placed in the center zone.</summary>
        private static readonly HashSet<string> FillItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "stacks", "tasks" };

        private readonly BarHost _host;
        private readonly List<ICairoWidget> _widgets = new List<ICairoWidget>();
        private readonly List<FrameworkElement> _placed = new List<FrameworkElement>();
        private readonly Dictionary<MenuItem, Menu> _menuWrappers = new Dictionary<MenuItem, Menu>();
        private readonly Dictionary<FrameworkElement, string> _ids = new Dictionary<FrameworkElement, string>();
        private Snapshot _snapshot;
        private Grid _zones;

        public BarCustomizer(BarHost host)
        {
            _host = host;
        }

        public BarHost Host => _host;

        public bool IsActive => _snapshot != null;

        /// <summary>Width taken by everything except the taskbar's task list, or null when the layout isn't customized.</summary>
        public double? ReservedWidth
        {
            get
            {
                if (!IsActive) return null;
                double width = 0;
                foreach (var pair in _ids)
                {
                    if (string.Equals(pair.Value, "tasks", StringComparison.OrdinalIgnoreCase)) continue;
                    width += pair.Key.ActualWidth + pair.Key.Margin.Left + pair.Key.Margin.Right;
                }
                return width;
            }
        }

        /// <summary>Applies the layout from <paramref name="bar"/>; restores the stock layout when there is none.</summary>
        public void Apply(BarConfig bar, ResolvedSettings settings, WidgetRegistry registry, ActionRunner actions, Action<string> problem)
        {
            Restore();

            var layout = bar?.Layout;
            if (layout == null || (layout.Left == null && layout.Center == null && layout.Right == null))
            {
                return;
            }

            var items = _host.GetItems().Where(i => i.Value != null).ToList();
            var byId = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items) byId[item.Key] = item.Value;

            _snapshot = Snapshot.Take(_host.Container, items.Select(i => i.Value));

            var defaults = DefaultZones(items.Select(i => i.Key).ToList());
            var explicitIds = new HashSet<string>(
                (layout.Left ?? new List<string>()).Concat(layout.Center ?? new List<string>()).Concat(layout.Right ?? new List<string>()),
                StringComparer.OrdinalIgnoreCase);

            // A zone left out of the config keeps its default contents, minus anything placed explicitly elsewhere.
            List<string> Zone(List<string> configured, List<string> fallback) =>
                configured ?? fallback.Where(id => !explicitIds.Contains(id)).ToList();

            var left = Zone(layout.Left, defaults.Item1);
            var center = Zone(layout.Center, defaults.Item2);
            var right = Zone(layout.Right, defaults.Item3);

            // Everything managed leaves its stock position; unlisted items are thereby hidden.
            foreach (var element in byId.Values) Detach(element);

            _zones = new Grid { Name = "CairoPlusZones", VerticalAlignment = VerticalAlignment.Stretch };
            _zones.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _zones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _zones.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftPanel = NewStack(HorizontalAlignment.Left);
            var rightPanel = NewStack(HorizontalAlignment.Right);
            Grid.SetColumn(leftPanel, 0);
            Grid.SetColumn(rightPanel, 2);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var leftElements = Resolve(left, byId, settings, registry, actions, problem, seen);
            var centerElements = Resolve(center, byId, settings, registry, actions, problem, seen);
            var rightElements = Resolve(right, byId, settings, registry, actions, problem, seen);

            foreach (var e in leftElements) leftPanel.Children.Add(e);
            foreach (var e in rightElements) rightPanel.Children.Add(e);

            Panel centerPanel = BuildCenter(center, centerElements);
            Grid.SetColumn(centerPanel, 1);

            _zones.Children.Add(leftPanel);
            _zones.Children.Add(centerPanel);
            _zones.Children.Add(rightPanel);

            foreach (string id in left.Concat(right))
            {
                if (FillItems.Contains(id))
                {
                    problem($"{BarName}: '{id}' only stretches in the center zone; it is shown at its natural size.");
                }
            }

            // Stock children the layout doesn't manage (e.g. the taskbar's task list popup) stay where they are;
            // the zones fill whatever space the container gives its last child.
            _host.Container.Children.Add(_zones);

            leftPanel.SizeChanged += OnZoneSizeChanged;
            rightPanel.SizeChanged += OnZoneSizeChanged;
            centerPanel.SizeChanged += OnZoneSizeChanged;
            _host.LayoutChanged?.Invoke();
        }

        private string BarName => _host.Kind == WidgetBar.Taskbar ? "taskbar.layout" : "menuBar.layout";

        private void OnZoneSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) _host.LayoutChanged?.Invoke();
        }

        private Panel BuildCenter(List<string> ids, List<FrameworkElement> elements)
        {
            int fillIndex = elements.FindIndex(e => _ids.TryGetValue(e, out string id) && FillItems.Contains(id));
            if (fillIndex < 0)
            {
                var stack = NewStack(HorizontalAlignment.Center);
                foreach (var e in elements) stack.Children.Add(e);
                return stack;
            }

            // Items before the fill item dock left, items after it dock right, the fill item takes the rest.
            var dock = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Stretch };
            for (int i = 0; i < fillIndex; i++)
            {
                DockPanel.SetDock(elements[i], Dock.Left);
                dock.Children.Add(elements[i]);
            }

            for (int i = elements.Count - 1; i > fillIndex; i--)
            {
                DockPanel.SetDock(elements[i], Dock.Right);
                dock.Children.Add(elements[i]);
            }

            dock.Children.Add(elements[fillIndex]);
            return dock;
        }

        private List<FrameworkElement> Resolve(List<string> ids, Dictionary<string, FrameworkElement> byId, ResolvedSettings settings,
            WidgetRegistry registry, ActionRunner actions, Action<string> problem, HashSet<string> seen)
        {
            var result = new List<FrameworkElement>();

            foreach (string rawId in ids)
            {
                string id = (rawId ?? "").Trim();
                if (id.Length == 0) continue;

                if (!seen.Add(id))
                {
                    problem($"{BarName}: '{id}' is listed more than once; only the first is used.");
                    continue;
                }

                if (id.StartsWith(WidgetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string widgetId = id.Substring(WidgetPrefix.Length);
                    var widget = registry.Create(widgetId, settings.Widgets, _host.Kind, _host.Window.Dispatcher, problem, out string error);
                    if (widget == null)
                    {
                        problem($"{BarName}: {error}");
                        continue;
                    }

                    _widgets.Add(widget);
                    var hosted = WrapWidget(widget, widgetId, settings, actions, problem);
                    _ids[hosted] = id;
                    _placed.Add(hosted);
                    result.Add(hosted);
                    continue;
                }

                if (!byId.TryGetValue(id, out FrameworkElement element))
                {
                    if (KnownIds(_host.Kind).Contains(id))
                    {
                        problem($"{BarName}: '{id}' is turned off in Cairo's settings, so it can't be shown.");
                        continue;
                    }

                    problem($"{BarName}: unknown item '{id}' (available: {string.Join(", ", byId.Keys)}, or widget:<id>).");
                    continue;
                }

                FrameworkElement placed = element;
                if (element is MenuItem menuItem)
                {
                    // Top-level menu items only behave as menu headers inside a Menu.
                    var wrapper = new Menu { VerticalAlignment = VerticalAlignment.Top };
                    wrapper.SetResourceReference(FrameworkElement.StyleProperty, "CairoMenuBarMainContainerStyle");
                    wrapper.Items.Add(menuItem);
                    _menuWrappers[menuItem] = wrapper;
                    placed = wrapper;
                }

                _ids[placed] = id;
                _placed.Add(element);
                result.Add(placed);
            }

            return result;
        }

        private FrameworkElement WrapWidget(ICairoWidget widget, string widgetId, ResolvedSettings settings, ActionRunner actions, Action<string> problem)
        {
            var host = new Border
            {
                Child = widget.View,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Transparent,
                ToolTip = widget.View.ToolTip
            };
            widget.View.VerticalAlignment = VerticalAlignment.Center;

            // Optional "onClick": { "action": ..., "arg": ..., "args": ... }
            if (settings.Widgets.TryGetValue(widgetId, out JsonElement def) && def.ValueKind == JsonValueKind.Object &&
                TryGetProperty(def, "onClick", out JsonElement click) && click.ValueKind == JsonValueKind.Object)
            {
                string action = GetString(click, "action");
                string arg = GetString(click, "arg");
                string args = GetString(click, "args");
                string error = ActionRunner.Validate(action, arg);
                if (error != null)
                {
                    problem($"widget '{widgetId}' onClick: {error}");
                }
                else
                {
                    host.Cursor = Cursors.Hand;
                    host.MouseLeftButtonUp += (s, e) =>
                    {
                        string runError = actions.Run(action, arg, args);
                        if (runError != null) problem($"widget '{widgetId}' onClick: {runError}");
                    };
                }
            }

            host.MouseEnter += (s, e) => host.SetResourceReference(Border.BackgroundProperty, WidgetTheme.HoverBrushKey);
            host.MouseLeave += (s, e) => host.Background = System.Windows.Media.Brushes.Transparent;
            return host;
        }

        private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string GetString(JsonElement obj, string name)
        {
            return TryGetProperty(obj, name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        /// <summary>Puts the bar back exactly as Cairo built it and disposes widgets.</summary>
        public void Restore()
        {
            foreach (var widget in _widgets)
            {
                try { widget.Dispose(); } catch { /* a misbehaving widget must not block restoring the bar */ }
            }

            _widgets.Clear();

            if (_snapshot == null) return;

            foreach (var element in _placed) Detach(element);
            foreach (var pair in _menuWrappers) pair.Value.Items.Remove(pair.Key);
            _menuWrappers.Clear();
            _placed.Clear();
            _ids.Clear();

            _snapshot.Restore();
            _snapshot = null;
            _zones = null;
            _host.LayoutChanged?.Invoke();
        }

        private static readonly HashSet<string> MenuBarIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "cairoMenu", "programsMenu", "placesMenu", "stacks", "tray", "volume", "actionCenter", "clock", "search" };

        private static readonly HashSet<string> TaskbarIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "desktopButton", "quickLaunch", "tasks", "taskList" };

        /// <summary>Every built-in id for a bar, including ones that may currently be switched off.</summary>
        internal static HashSet<string> KnownIds(WidgetBar bar) => bar == WidgetBar.Taskbar ? TaskbarIds : MenuBarIds;

        private static StackPanel NewStack(HorizontalAlignment alignment)
        {
            return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Stretch };
        }

        /// <summary>Stock zone contents, used for zones the config leaves out.</summary>
        private Tuple<List<string>, List<string>, List<string>> DefaultZones(List<string> available)
        {
            List<string> left, center;
            if (_host.Kind == WidgetBar.Taskbar)
            {
                left = new List<string> { "desktopButton", "quickLaunch" };
                center = new List<string> { "tasks" };
            }
            else
            {
                left = new List<string> { "cairoMenu", "programsMenu", "placesMenu" };
                center = new List<string> { "stacks" };
            }

            // Everything else (menu extras, task list button, third-party extensions) goes right, in stock order.
            var right = available.Where(id => !left.Contains(id, StringComparer.OrdinalIgnoreCase) && !center.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            return Tuple.Create(left, center, right);
        }

        internal static void Detach(FrameworkElement element)
        {
            switch (element.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;
                case ItemsControl items:
                    items.Items.Remove(element);
                    break;
                case ContentControl content:
                    content.Content = null;
                    break;
                case Decorator decorator:
                    decorator.Child = null;
                    break;
            }
        }

        /// <summary>Original parents and positions of everything the layout moves.</summary>
        private sealed class Snapshot
        {
            private Panel _container;
            private List<UIElement> _containerChildren;
            private readonly List<Tuple<FrameworkElement, object, int>> _placements = new List<Tuple<FrameworkElement, object, int>>();
            private readonly Dictionary<FrameworkElement, object> _docks = new Dictionary<FrameworkElement, object>();

            public static Snapshot Take(Panel container, IEnumerable<FrameworkElement> elements)
            {
                var snapshot = new Snapshot
                {
                    _container = container,
                    _containerChildren = container.Children.Cast<UIElement>().ToList()
                };

                foreach (var element in elements)
                {
                    snapshot._docks[element] = element.ReadLocalValue(DockPanel.DockProperty);

                    switch (element.Parent)
                    {
                        case Panel panel when panel != container:
                            snapshot._placements.Add(Tuple.Create(element, (object)panel, panel.Children.IndexOf(element)));
                            break;
                        case ItemsControl items:
                            snapshot._placements.Add(Tuple.Create(element, (object)items, items.Items.IndexOf(element)));
                            break;
                    }
                }

                return snapshot;
            }

            public void Restore()
            {
                // Inner parents first, lowest index first, so each insert lands where it was.
                foreach (var placement in _placements.OrderBy(p => p.Item3))
                {
                    Detach(placement.Item1);
                    if (placement.Item2 is Panel panel)
                    {
                        panel.Children.Insert(Math.Min(placement.Item3, panel.Children.Count), placement.Item1);
                    }
                    else if (placement.Item2 is ItemsControl items)
                    {
                        items.Items.Insert(Math.Min(placement.Item3, items.Items.Count), placement.Item1);
                    }
                }

                foreach (var dock in _docks)
                {
                    if (dock.Value == DependencyProperty.UnsetValue) dock.Key.ClearValue(DockPanel.DockProperty);
                    else dock.Key.SetValue(DockPanel.DockProperty, dock.Value);
                }

                foreach (var child in _containerChildren.OfType<FrameworkElement>()) Detach(child);
                _container.Children.Clear();
                foreach (var child in _containerChildren) _container.Children.Add(child);
            }
        }
    }
}
