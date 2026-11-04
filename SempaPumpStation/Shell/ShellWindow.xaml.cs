using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace SempaPumpStation.Shell
{
    public partial class ShellWindow : Window
    {
        private const double CaptionHeight = 54;
        private bool _fullScreen;

        public ShellWindow()
        {
            InitializeComponent();

            // Full screen on the monitor the window opens on, over the taskbar. The ▢ button and F11 switch to a
            // normal window and back.
            SourceInitialized += (s, e) => SetFullScreen(true);
            KeyDown += (s, e) => { if (e.Key == Key.F11) { SetFullScreen(!_fullScreen); e.Handled = true; } };
        }

        public bool IsFullScreen => _fullScreen;

        /// <summary>
        /// Full screen = a normal (not maximized) window with exactly the monitor's bounds: a maximized window with
        /// WindowChrome stops at the taskbar, a window that covers the whole monitor is shown over it.
        /// </summary>
        public void SetFullScreen(bool on)
        {
            _fullScreen = on;
            var chrome = WindowChrome.GetWindowChrome(this);
            WindowState = WindowState.Normal;

            if (on)
            {
                ResizeMode = ResizeMode.NoResize;
                if (chrome != null) chrome.CaptionHeight = 0;   // the header must not drag the window off the screen
                var bounds = MonitorBounds();
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;
            }
            else
            {
                ResizeMode = ResizeMode.CanResize;
                if (chrome != null) chrome.CaptionHeight = CaptionHeight;
                var work = SystemParameters.WorkArea;
                Width = Math.Min(1600, work.Width);
                Height = Math.Min(1000, work.Height);
                Left = work.Left + (work.Width - Width) / 2;
                Top = work.Top + (work.Height - Height) / 2;
            }
        }

        // The bounds of the monitor the window is on, in WPF units
        private Rect MonitorBounds()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info))
                return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

            var r = info.Monitor;
            var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = toDip.Transform(new Point(r.Left, r.Top));
            var bottomRight = toDip.Transform(new Point(r.Right, r.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Max_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ---------------------------------------------------------------- Win32
        private const int MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
