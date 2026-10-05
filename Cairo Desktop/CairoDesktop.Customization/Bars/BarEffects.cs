using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CairoDesktop.Customization.Config;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;

namespace CairoDesktop.Customization.Bars
{
    /// <summary>
    /// Shape and effect settings for one bar: floating margin, corner radius, blur and animations.
    /// (Background opacity is done in the theme layer; item spacing in the layout.)
    /// Every change is recorded so <see cref="Restore"/> returns the bar to Cairo's own look.
    /// </summary>
    internal sealed class BarEffects
    {
        private readonly BarHost _host;
        private readonly AppBarWindow _appBar;

        private bool _active;
        private object _savedSurfaceMargin, _savedCornerRadius, _savedSurfaceBorder, _savedWindowBorder;
        private bool _blurChanged, _clipSet;
        private double _cornerRadius;
        private bool _slideInDone;

        public BarEffects(BarHost host)
        {
            _host = host;
            _appBar = host.Window as AppBarWindow;
        }

        /// <summary>Extra reserved height for a floating bar (margin above and below it).</summary>
        public double ExtraThickness { get; private set; }

        /// <summary>Width a floating bar gives up at its sides.</summary>
        public double HorizontalInset { get; private set; }

        public void Apply(BarConfig bar, AnimationConfig animations, Action<string> problem, string barName)
        {
            Restore();
            if (bar == null && (animations?.Enabled != true)) return;

            var surface = _host.Surface;
            bool floating = bar?.Floating == true;
            double margin = floating ? Math.Max(0, bar.Margin ?? 6) : 0;
            double radius = Math.Max(0, bar?.CornerRadius ?? 0);
            bool shaped = floating || radius > 0;

            _active = true;
            _savedSurfaceMargin = surface.ReadLocalValue(FrameworkElement.MarginProperty);
            _savedWindowBorder = _host.Window.ReadLocalValue(Control.BorderThicknessProperty);

            // The window's own edge line would sit outside a floating / rounded bar. Its thickness is folded into
            // the margin so the bar's content keeps exactly the height Cairo laid it out for.
            Thickness windowBorder = shaped ? _host.Window.BorderThickness : new Thickness(0);

            if (floating)
            {
                ExtraThickness = margin * 2;
                HorizontalInset = margin * 2;
                surface.Margin = new Thickness(margin + windowBorder.Left, margin + windowBorder.Top, margin + windowBorder.Right, margin + windowBorder.Bottom);
                _host.RefreshThickness?.Invoke();
            }
            else if (shaped)
            {
                surface.Margin = windowBorder;
            }

            if (shaped)
            {
                _host.Window.BorderThickness = new Thickness(0);

                if (surface is Border border)
                {
                    _savedCornerRadius = border.ReadLocalValue(Border.CornerRadiusProperty);
                    _savedSurfaceBorder = border.ReadLocalValue(Border.BorderThicknessProperty);
                    border.CornerRadius = new CornerRadius(radius);
                    if (floating) border.BorderThickness = new Thickness(1);
                }

                _cornerRadius = radius;
                surface.SizeChanged += Surface_SizeChanged;
                _clipSet = true;
                UpdateClip();
            }

            ApplyBlur(bar?.Blur, shaped, problem, barName);
            ApplyAnimations(animations);
        }

        #region Shape
        private void Surface_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClip();
        }

        private void UpdateClip()
        {
            var surface = _host.Surface;
            if (_cornerRadius <= 0)
            {
                surface.ClearValue(UIElement.ClipProperty);
                return;
            }

            // Clip children (task buttons, menu headers) to the rounded shape too.
            surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), _cornerRadius, _cornerRadius);
        }
        #endregion

        #region Blur
        private void ApplyBlur(bool? blur, bool shaped, Action<string> problem, string barName)
        {
            if (blur == null || _appBar == null || _appBar.Handle == IntPtr.Zero) return;

            if (blur.Value && shaped)
            {
                // Windows 10's blur-behind always covers the whole window rectangle, and clipping the window to a
                // rounded region makes WPF's layered bar window stop drawing (tested on 21H2). So a floating or
                // rounded bar keeps its shape and goes without blur.
                problem($"{barName}: blur was turned off because this bar is floating or has rounded corners; Windows 10 can't clip blur to that shape.");
                WindowHelper.SetWindowBlur(_appBar.Handle, false);
                _blurChanged = true;
                return;
            }

            WindowHelper.SetWindowBlur(_appBar.Handle, blur.Value);
            _blurChanged = true;
        }
        #endregion

        #region Animations
        private void ApplyAnimations(AnimationConfig animations)
        {
            if (animations?.Enabled != true) return;

            var duration = TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(2000, animations.DurationMs ?? 220)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var surface = _host.Surface;

            // Fade in on every (re)apply, so theme switches cross-fade instead of snapping.
            surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, duration) { EasingFunction = ease });

            // Slide in from the screen edge the first time this bar is shown.
            if (!_slideInDone)
            {
                _slideInDone = true;
                double distance = Math.Max(_host.Window.ActualHeight, 24);
                bool fromTop = _appBar?.AppBarEdge == AppBarEdge.Top;
                var slide = new TranslateTransform(0, fromTop ? -distance : distance);
                surface.RenderTransform = slide;
                var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 1.6)) { EasingFunction = ease };
                animation.Completed += (s, e) => surface.ClearValue(UIElement.RenderTransformProperty);
                slide.BeginAnimation(TranslateTransform.YProperty, animation);
            }
        }
        #endregion

        public void Restore()
        {
            if (!_active) return;
            _active = false;

            var surface = _host.Surface;
            surface.SizeChanged -= Surface_SizeChanged;
            surface.BeginAnimation(UIElement.OpacityProperty, null);

            RestoreLocal(surface, FrameworkElement.MarginProperty, _savedSurfaceMargin);
            RestoreLocal(_host.Window, Control.BorderThicknessProperty, _savedWindowBorder);
            if (surface is Border border)
            {
                if (_savedCornerRadius != null) RestoreLocal(border, Border.CornerRadiusProperty, _savedCornerRadius);
                if (_savedSurfaceBorder != null) RestoreLocal(border, Border.BorderThicknessProperty, _savedSurfaceBorder);
            }

            _savedCornerRadius = _savedSurfaceBorder = null;

            if (_clipSet)
            {
                surface.ClearValue(UIElement.ClipProperty);
                _clipSet = false;
            }


            if (_blurChanged && _appBar != null)
            {
                // Hand blur back to Cairo's own setting (it re-applies it on its next settings change).
                var cairoSettings = CairoDesktop.Common.Settings.Instance;
                bool cairoBlur = cairoSettings.EnableMenuBarBlur &&
                                 (_host.Kind == CairoDesktop.Widgets.Sdk.WidgetBar.MenuBar || cairoSettings.FullWidthTaskBar);
                WindowHelper.SetWindowBlur(_appBar.Handle, cairoBlur);
                _blurChanged = false;
            }

            bool hadThickness = ExtraThickness != 0;
            ExtraThickness = 0;
            HorizontalInset = 0;
            _cornerRadius = 0;
            if (hadThickness) _host.RefreshThickness?.Invoke();
        }

        private static void RestoreLocal(DependencyObject target, DependencyProperty property, object saved)
        {
            if (saved == DependencyProperty.UnsetValue || saved == null) target.ClearValue(property);
            else target.SetValue(property, saved);
        }
    }
}
