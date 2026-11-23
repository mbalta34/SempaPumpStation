using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>A monitor of this PC, in physical pixels (the Win32 desktop coordinates).</summary>
    public sealed class DisplayMonitor
    {
        /// <summary>Windows' device name, e.g. \\.\DISPLAY2: what a TV assignment is saved as.</summary>
        public string DeviceName { get; set; }
        /// <summary>1, 2, … from left to right (top to bottom for equal left edges).</summary>
        public int Number { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }

        public string Display => "Ekran " + Number + " — " + Width + " × " + Height + (IsPrimary ? " (ana ekran)" : "");
    }

    /// <summary>The monitors of this PC (EnumDisplayMonitors), and placing a window on one of them.</summary>
    public static class DisplayMonitors
    {
        /// <summary>Every monitor, numbered from left to right.</summary>
        public static IReadOnlyList<DisplayMonitor> All()
        {
            var list = new List<DisplayMonitor>();
            MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf(typeof(MonitorInfoEx)) };
                if (GetMonitorInfo(monitor, ref info))
                    list.Add(new DisplayMonitor
                    {
                        DeviceName = info.Device,
                        Left = info.Monitor.Left,
                        Top = info.Monitor.Top,
                        Width = info.Monitor.Right - info.Monitor.Left,
                        Height = info.Monitor.Bottom - info.Monitor.Top,
                        IsPrimary = (info.Flags & MonitorInfoPrimary) != 0,
                    });
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            var ordered = list.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
            return ordered;
        }

        public static DisplayMonitor Find(string deviceName) =>
            string.IsNullOrEmpty(deviceName) ? null : All().FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Puts the window over the whole monitor. Done in physical pixels through the window handle, so it also lands right
        /// when the monitors have different scaling; the window is then maximised on that monitor.
        /// </summary>
        public static void FillMonitor(Window window, DisplayMonitor monitor)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowPos(handle, IntPtr.Zero, monitor.Left, monitor.Top, monitor.Width, monitor.Height, SwpNoZOrder | SwpNoActivate);
        }

        // ---------------------------------------------------------------- Win32
        private const int MonitorInfoPrimary = 1;
        private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
