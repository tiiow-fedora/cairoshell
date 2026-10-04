using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CairoDesktop.Widgets.Sdk
{
    /// <summary>
    /// Base class for the common case: a themed line of text refreshed on a timer.
    /// Override <see cref="GetText"/> (and optionally <see cref="GetToolTip"/>).
    /// </summary>
    public abstract class TextWidget : ICairoWidget
    {
        private readonly DispatcherTimer _timer;
        private bool _reportedError;

        /// <summary>The TextBlock shown on the bar.</summary>
        protected TextBlock TextBlock { get; }

        /// <summary>The context this widget was created with.</summary>
        protected WidgetContext Context { get; }

        /// <param name="context">The context passed to your factory.</param>
        /// <param name="interval">How often <see cref="GetText"/> runs. TimeSpan.Zero = only once.</param>
        protected TextWidget(WidgetContext context, TimeSpan interval)
        {
            Context = context;
            TextBlock = new TextBlock();
            WidgetTheme.ApplyText(TextBlock, context.Bar);

            if (interval > TimeSpan.Zero)
            {
                _timer = new DispatcherTimer(interval, DispatcherPriority.Background, (s, e) => Refresh(), context.Dispatcher);
                _timer.Start();
            }

            // First refresh once the view is on screen, so constructors of derived classes have run.
            context.Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Loaded);
        }

        /// <inheritdoc />
        public FrameworkElement View => TextBlock;

        /// <summary>Returns the text to show. Runs on the UI thread, so keep it fast.</summary>
        protected abstract string GetText();

        /// <summary>Optional tooltip text; null for none.</summary>
        protected virtual string GetToolTip() => null;

        /// <summary>Re-runs <see cref="GetText"/> immediately.</summary>
        protected void Refresh()
        {
            try
            {
                string text = GetText();
                TextBlock.Text = text ?? "";
                TextBlock.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
                TextBlock.ToolTip = GetToolTip();
            }
            catch (Exception ex)
            {
                TextBlock.Text = "⚠";
                TextBlock.ToolTip = ex.Message;
                if (!_reportedError)
                {
                    _reportedError = true;
                    Context.Log(ex.Message);
                }
            }
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            _timer?.Stop();
        }
    }
}
