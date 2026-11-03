// Inert until CairoDesktop.Customization references CairoDesktop.Features and defines CAIRO_FEATURES
// (see docs/features/clipboard-history.md, "Wiring status"). Guarded so an unwired checkout still builds.
#if CAIRO_FEATURES
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CairoDesktop.Features.ClipboardHistory;

namespace CairoDesktop.Customization.Features.ClipboardHistory
{
    /// <summary>
    /// Watches the clipboard through a hidden message-only window and reports text clips. UI thread only.
    /// </summary>
    internal sealed class ClipboardMonitor : IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // Another process often still holds the clipboard open when WM_CLIPBOARDUPDATE arrives; the first read then fails
        // with CLIPBRD_E_CANT_OPEN (a COMException). Retry a few times on a DispatcherTimer. Never Thread.Sleep on the UI thread.
        private static readonly int[] RetryDelaysMs = { 25, 50, 100, 200, 400 };

        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _retryTimer;
        private HwndSource _source;
        private int _attempt;
        private uint _ownSequence;      // sequence number right after our own SetText: updates with it are our write
        private uint _lastSeenSequence;
        private bool _disposed;

        public ClipboardMonitor(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _retryTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _retryTimer.Tick += OnRetryTick;
        }

        /// <summary>Raised on the UI thread with the clip text, owning process name (may be null) and password-manager markers.</summary>
        public event Action<string, string, ClipboardFormatFlags> TextCaptured;

        public void Start()
        {
            if (_source != null || _disposed) return;
            var p = new HwndSourceParameters("CairoClipboardMonitor") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            if (!AddClipboardFormatListener(_source.Handle))
            {
                // Without the listener we would never hear about changes; fail loudly rather than look alive.
                int error = Marshal.GetLastWin32Error();
                Teardown();
                throw new InvalidOperationException("AddClipboardFormatListener failed (Win32 error " + error + ").");
            }
        }

        /// <summary>Puts text on the clipboard without it being recorded again as a new clip.</summary>
        public bool SetText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                // Tiny race: if another app copies between SetText and this call we would skip its clip. Acceptable.
                _ownSequence = GetClipboardSequenceNumber();
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return false; // clipboard busy; the caller just keeps the popup open
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                handled = true;
                _attempt = 0;
                // Several updates arrive per copy (one per format); restarting the timer coalesces them.
                _retryTimer.Stop();
                _retryTimer.Interval = TimeSpan.FromMilliseconds(RetryDelaysMs[0]);
                _retryTimer.Start();
            }

            return IntPtr.Zero;
        }

        private void OnRetryTick(object sender, EventArgs e)
        {
            _retryTimer.Stop();
            if (_disposed) return;

            uint seq = GetClipboardSequenceNumber();
            if (seq == _ownSequence || seq == _lastSeenSequence) return;

            string text;
            ClipboardFormatFlags flags;
            try
            {
                if (!Clipboard.ContainsText()) { _lastSeenSequence = seq; return; }
                flags = ReadFlags(Clipboard.GetDataObject());
                text = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (++_attempt < RetryDelaysMs.Length)
                {
                    _retryTimer.Interval = TimeSpan.FromMilliseconds(RetryDelaysMs[_attempt]);
                    _retryTimer.Start();
                }

                return; // out of retries: skip this clip rather than block or crash
            }

            _lastSeenSequence = seq;
            TextCaptured?.Invoke(text, GetOwnerProcessName(), flags);
        }

        private static ClipboardFormatFlags ReadFlags(IDataObject data)
        {
            if (data == null) return ClipboardFormatFlags.None;
            bool exclude = data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing");
            bool noHistory = data.GetDataPresent("CanIncludeInClipboardHistory") && ReadDword(data.GetData("CanIncludeInClipboardHistory")) == 0;
            bool noCloud = data.GetDataPresent("CanUploadToCloudClipboard") && ReadDword(data.GetData("CanUploadToCloudClipboard")) == 0;
            return new ClipboardFormatFlags(exclude, noHistory, noCloud);
        }

        private static int ReadDword(object value)
        {
            // Password managers store a 4-byte DWORD; WPF hands it back as a stream, byte[] or boxed int.
            if (value is int i) return i;
            if (value is byte[] b && b.Length >= 4) return BitConverter.ToInt32(b, 0);
            if (value is Stream s && s.CanRead)
            {
                var buf = new byte[4];
                return s.Read(buf, 0, 4) == 4 ? BitConverter.ToInt32(buf, 0) : -1;
            }

            return -1; // unknown shape: treat as "allowed" (only an explicit 0 blocks)
        }

        private static string GetOwnerProcessName()
        {
            try
            {
                IntPtr owner = GetClipboardOwner();
                if (owner == IntPtr.Zero) return null;
                GetWindowThreadProcessId(owner, out uint pid);
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch (Exception)
            {
                return null; // exited, elevated or inaccessible process: unknown source is fine
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_dispatcher.CheckAccess()) Teardown();
            else _dispatcher.Invoke(Teardown); // HwndSource must be destroyed on the thread that created it
        }

        private void Teardown()
        {
            _retryTimer.Stop();
            _retryTimer.Tick -= OnRetryTick;
            HwndSource source = _source;
            _source = null;
            if (source == null) return;
            try { RemoveClipboardFormatListener(source.Handle); } catch (Exception) { /* window may already be gone */ }
            source.RemoveHook(WndProc);
            source.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
#endif
