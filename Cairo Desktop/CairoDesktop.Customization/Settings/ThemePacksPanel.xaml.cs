using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CairoDesktop.Customization.Themes;

namespace CairoDesktop.Customization.Settings
{
    /// <summary>"Theme Packs" tab in Cairo's settings window.</summary>
    public partial class ThemePacksPanel : UserControl
    {
        private const string NoneId = "";

        public ThemePacksPanel()
        {
            InitializeComponent();
        }

        private static CairoPlusService Service => CairoPlusService.Current;

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (Service != null)
            {
                Service.Applied += Service_Applied;
            }

            Refresh();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (Service != null)
            {
                Service.Applied -= Service_Applied;
            }
        }

        private void Service_Applied(object sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            var service = Service;
            if (service == null)
            {
                IsEnabled = false;
                txtStatus.Text = "Cairo Plus is not running.";
                return;
            }

            chkEnabled.IsChecked = service.Settings.Enabled;

            string activeId = service.Settings.Pack?.Id ?? NoneId;
            string selectedId = (lstPacks.SelectedItem as PackItem)?.Id ?? activeId;

            var items = new List<PackItem>
            {
                new PackItem
                {
                    Id = NoneId,
                    Name = "None",
                    Subtitle = "Use Cairo's own theme from the General tab",
                    IsActive = service.Settings.Enabled && activeId == NoneId
                }
            };

            items.AddRange(service.GetThemePacks().Select(p => PackItem.From(p, service.Settings.Enabled && p.Id == activeId)));
            lstPacks.ItemsSource = items;
            lstPacks.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? items[0];

            var status = new StringBuilder();
            status.AppendLine(service.Settings.Enabled
                ? $"Active theme pack: {service.Settings.Pack?.DisplayName ?? "none"}"
                : "Cairo Plus is off. Applying a theme pack turns it on.");
            status.AppendLine($"Config: {CairoPlusPaths.ConfigFile}");
            foreach (string hotkey in service.ActiveHotkeys)
            {
                status.AppendLine("Hotkey: " + hotkey);
            }

            foreach (string problem in service.Problems)
            {
                status.AppendLine("• " + problem);
            }

            txtStatus.Text = status.ToString().TrimEnd();
        }

        private void ApplySelected()
        {
            if (Service == null || !(lstPacks.SelectedItem is PackItem item)) return;

            if (item.Error != null)
            {
                MessageBox.Show(item.Error, "Theme pack can't be applied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Service.ApplyTheme(item.Id == NoneId ? null : item.Id);
        }

        private void btnApply_Click(object sender, RoutedEventArgs e) => ApplySelected();

        private void lstPacks_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ApplySelected();

        private void chkEnabled_Click(object sender, RoutedEventArgs e)
        {
            Service?.SetEnabled(chkEnabled.IsChecked == true);
        }

        private void btnReload_Click(object sender, RoutedEventArgs e)
        {
            Service?.Reload();
        }

        private void btnOpenConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(CairoPlusPaths.ConfigFile))
            {
                CairoPlusService.WriteConfigProperties(null, setTheme: false, enabled: null);
            }

            Open(CairoPlusPaths.ConfigFile);
        }

        private void btnOpenPacks_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(CairoPlusPaths.UserThemePacksFolder);
            Open(CairoPlusPaths.UserThemePacksFolder);
        }

        private static void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Cairo Plus", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private sealed class PackItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Subtitle { get; set; }
            public string Error { get; set; }
            public bool IsActive { get; set; }
            public List<Brush> Swatches { get; set; } = new List<Brush>();

            public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
            public Visibility ErrorVisibility => Error != null ? Visibility.Visible : Visibility.Collapsed;

            public static PackItem From(ThemePack pack, bool active)
            {
                var item = new PackItem { Id = pack.Id, Name = pack.DisplayName, IsActive = active, Error = pack.Error };
                var manifest = pack.Manifest;
                if (manifest == null) return item;

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(manifest.Author)) parts.Add("by " + manifest.Author);
                if (!string.IsNullOrWhiteSpace(manifest.Description)) parts.Add(manifest.Description);
                item.Subtitle = string.Join(" · ", parts);

                if (manifest.Palette != null)
                {
                    var roles = PaletteExpander.Expand(manifest.Palette).Roles;
                    foreach (string role in new[] { "background", "surface", "text", "accent", "urgent" })
                    {
                        var brush = new SolidColorBrush(roles[role]);
                        brush.Freeze();
                        item.Swatches.Add(brush);
                    }
                }

                return item;
            }
        }
    }
}
