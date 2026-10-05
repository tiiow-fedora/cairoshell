using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CairoDesktop.Customization.Bars;
using CairoDesktop.Customization.Config;
using CairoDesktop.Customization.Widgets;
using CairoDesktop.Widgets.Sdk;
using Xunit;

namespace CairoDesktop.Customization.Tests
{
    public class BarLayoutTests
    {
        /// <summary>Runs WPF code on an STA thread and rethrows assertion failures.</summary>
        private static void OnSta(Action action)
        {
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        /// <summary>A miniature menu bar shaped like Cairo's: [StackPanel{Menu{3 items}}, extras StackPanel (right), stacks (fill)].</summary>
        private sealed class FakeMenuBar
        {
            public readonly Window Window = new Window { Width = 1000, Height = 30 };
            public readonly DockPanel Container = new DockPanel();
            public readonly Menu Menu = new Menu();
            public readonly MenuItem CairoMenu = new MenuItem { Header = "C", Width = 40 };
            public readonly MenuItem Programs = new MenuItem { Header = "Programs", Width = 80 };
            public readonly MenuItem Places = new MenuItem { Header = "Places", Width = 60 };
            public readonly StackPanel Extras = new StackPanel { Orientation = Orientation.Horizontal };
            public readonly Border Clock = new Border { Width = 70 };
            public readonly Border Stacks = new Border { MinWidth = 50 };
            public readonly StackPanel MenuHost = new StackPanel { Orientation = Orientation.Horizontal };

            public FakeMenuBar()
            {
                Menu.Items.Add(CairoMenu);
                Menu.Items.Add(Programs);
                Menu.Items.Add(Places);
                MenuHost.Children.Add(Menu);
                Container.Children.Add(MenuHost);
                DockPanel.SetDock(Extras, Dock.Right);
                Extras.Children.Add(Clock);
                Container.Children.Add(Extras);
                Container.Children.Add(Stacks);
                Window.Content = Container;
            }

            public BarHost Host() => new BarHost(Window, WidgetBar.MenuBar, Container, () => new[]
            {
                new KeyValuePair<string, FrameworkElement>("cairoMenu", CairoMenu),
                new KeyValuePair<string, FrameworkElement>("programsMenu", Programs),
                new KeyValuePair<string, FrameworkElement>("placesMenu", Places),
                new KeyValuePair<string, FrameworkElement>("stacks", Stacks),
                new KeyValuePair<string, FrameworkElement>("clock", Clock)
            }, null);

            public void Layout()
            {
                Container.Measure(new Size(1000, 30));
                Container.Arrange(new Rect(0, 0, 1000, 30));
                Container.UpdateLayout();
            }
        }

        private static ResolvedSettings Settings(BarConfig menuBar)
        {
            var config = ConfigLoader.Parse("{ \"enabled\": true }").Config;
            config.MenuBar = menuBar;
            return ResolvedSettings.Resolve(config, null);
        }

        private static BarConfig Layout(string[] left, string[] center, string[] right) => new BarConfig
        {
            Layout = new ZoneLayout
            {
                Left = left == null ? null : new List<string>(left),
                Center = center == null ? null : new List<string>(center),
                Right = right == null ? null : new List<string>(right)
            }
        };

        [Fact]
        public void Restore_puts_menu_items_back_into_the_original_menu_and_they_render()
        {
            OnSta(() =>
            {
                var bar = new FakeMenuBar();
                bar.Layout();
                double stockMenuWidth = bar.Menu.DesiredSize.Width;
                Assert.True(stockMenuWidth > 0);

                var customizer = new BarCustomizer(bar.Host());
                var config = Layout(new[] { "cairoMenu", "programsMenu", "placesMenu", "stacks" }, new string[0], new[] { "clock" });
                customizer.Apply(config, Settings(config), new WidgetRegistry(), null, _ => { });
                bar.Layout();
                Assert.True(customizer.IsActive);
                Assert.Empty(bar.Menu.Items);

                customizer.Restore();
                bar.Layout();

                Assert.False(customizer.IsActive);
                Assert.Equal(new object[] { bar.CairoMenu, bar.Programs, bar.Places }, bar.Menu.Items.Cast());
                Assert.Same(bar.Menu, bar.CairoMenu.Parent);
                Assert.Equal(stockMenuWidth, bar.Menu.DesiredSize.Width);
                Assert.Equal(new UIElement[] { bar.MenuHost, bar.Extras, bar.Stacks }, bar.Container.Children.Cast());
                Assert.Same(bar.Extras, bar.Clock.Parent);
            });
        }

        [Fact]
        public void Apply_restore_apply_cycles_are_stable()
        {
            OnSta(() =>
            {
                var bar = new FakeMenuBar();
                bar.Layout();
                double stockMenuWidth = bar.Menu.DesiredSize.Width;
                var customizer = new BarCustomizer(bar.Host());
                var config = Layout(new[] { "cairoMenu", "programsMenu", "placesMenu", "stacks" }, new string[0], new[] { "clock" });

                for (int i = 0; i < 3; i++)
                {
                    customizer.Apply(config, Settings(config), new WidgetRegistry(), null, _ => { });
                    bar.Layout();
                    Assert.True(bar.Programs.IsArrangeValid);
                    Assert.True(bar.Programs.ActualWidth > 0);
                    customizer.Restore();
                    bar.Layout();
                    Assert.Equal(stockMenuWidth, bar.Menu.DesiredSize.Width);
                }
            });
        }

        [Fact]
        public void Switching_from_a_layout_to_floating_rounded_effects_keeps_menus_visible()
        {
            OnSta(() =>
            {
                var bar = new FakeMenuBar();
                bar.Layout();
                double stockMenuWidth = bar.Menu.DesiredSize.Width;
                var customizer = new BarCustomizer(bar.Host());

                var gruvbox = Layout(new[] { "cairoMenu", "programsMenu", "placesMenu", "stacks" }, new string[0], new[] { "clock" });
                gruvbox.Spacing = 4;
                customizer.Apply(gruvbox, Settings(gruvbox), new WidgetRegistry(), null, _ => { });
                bar.Layout();

                var tokyo = new BarConfig { Floating = true, Margin = 6, CornerRadius = 10, Opacity = 0.9 };
                var settings = Settings(tokyo);
                customizer.Apply(tokyo, settings, new WidgetRegistry(), null, _ => { });
                bar.Layout();

                Assert.Same(bar.Menu, bar.Programs.Parent);
                Assert.Equal(stockMenuWidth, bar.Menu.DesiredSize.Width);
                Assert.True(bar.MenuHost.ActualWidth > 0, "the menu host collapsed");
                Assert.True(bar.Stacks.TranslatePoint(new Point(), bar.Container).X >= bar.MenuHost.ActualWidth, "stacks overlap the menus");
            });
        }

        private static Style MenuStyle()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(Panel.IsItemsHostProperty, true);
            var style = new Style(typeof(Menu));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Menu)) { VisualTree = panel }));
            return style;
        }

        [Fact]
        public void Theme_reload_while_customized_does_not_strand_menu_items()
        {
            OnSta(() =>
            {
                var bar = new FakeMenuBar();
                bar.Window.Resources.MergedDictionaries.Add(new ResourceDictionary { ["CairoMenuBarMainContainerStyle"] = MenuStyle() });
                bar.Menu.Style = (Style)bar.Window.FindResource("CairoMenuBarMainContainerStyle");
                bar.Layout();
                double stockMenuWidth = bar.Menu.DesiredSize.Width;
                var customizer = new BarCustomizer(bar.Host());

                var config = Layout(new[] { "cairoMenu", "programsMenu", "placesMenu", "stacks" }, new string[0], new[] { "clock" });
                customizer.Apply(config, Settings(config), new WidgetRegistry(), null, _ => { });
                bar.Layout();

                // What Cairo's theme service does on a theme switch: clear the merged dictionaries, then add new ones.
                bar.Window.Resources.MergedDictionaries.Clear();
                bar.Layout();
                bar.Window.Resources.MergedDictionaries.Add(new ResourceDictionary { ["CairoMenuBarMainContainerStyle"] = MenuStyle() });
                bar.Layout();

                customizer.Restore();
                bar.Layout();

                Assert.Same(bar.Menu, bar.Programs.Parent);
                Assert.Same(bar.Menu, ItemsControl.ItemsControlFromItemContainer(bar.Programs));
                Assert.Equal(stockMenuWidth, bar.Menu.DesiredSize.Width);
            });
        }

        [Fact]
        public void Unlisted_items_are_hidden_and_omitted_zones_keep_defaults()
        {
            OnSta(() =>
            {
                var bar = new FakeMenuBar();
                var customizer = new BarCustomizer(bar.Host());
                // Only "right" given: left keeps the menus, center keeps stacks; clock moved explicitly.
                var config = Layout(null, null, new[] { "clock" });
                customizer.Apply(config, Settings(config), new WidgetRegistry(), null, _ => { });
                bar.Layout();

                Assert.NotNull(bar.Programs.Parent);
                Assert.NotNull(bar.Stacks.Parent);
                Assert.NotSame(bar.Extras, bar.Clock.Parent);

                // Now list only the clock everywhere: the menus and stacks disappear.
                config = Layout(new string[0], new string[0], new[] { "clock" });
                customizer.Apply(config, Settings(config), new WidgetRegistry(), null, _ => { });
                Assert.Null(bar.Programs.Parent);
                Assert.Null(bar.Stacks.Parent);
            });
        }
    }

    internal static class ItemCollectionExtensions
    {
        public static object[] Cast(this ItemCollection items)
        {
            var result = new object[items.Count];
            items.CopyTo(result, 0);
            return result;
        }

        public static UIElement[] Cast(this UIElementCollection children)
        {
            var result = new UIElement[children.Count];
            children.CopyTo(result, 0);
            return result;
        }
    }
}
