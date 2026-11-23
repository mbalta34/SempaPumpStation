using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>A monitor to choose for a TV output; an empty <see cref="DeviceName"/> is "off".</summary>
    public sealed class MonitorChoice
    {
        public string DeviceName { get; set; }
        public string Display { get; set; }
    }

    /// <summary>One zone's TV output: the monitor it is shown on.</summary>
    public sealed class TvOutputRow : ViewModelBase
    {
        private readonly TvOutputs _owner;
        public TvOutputRow(TvOutputs owner, int zone) { _owner = owner; Zone = zone; }

        public int Zone { get; }
        public string Title => "Bölge " + Zone;

        private string _monitor = "";
        /// <summary>The monitor's device name, "" = not shown. Choosing one opens the zone's TV picture there at once.</summary>
        public string Monitor
        {
            get => _monitor;
            set
            {
                value = value ?? "";
                if (value == _monitor || _owner.Refreshing) return;   // a combo box losing its item while the list is rebuilt
                _owner.Assign(Zone, value);
            }
        }

        internal void SetMonitor(string device) => Set(ref _monitor, device ?? "", nameof(Monitor));
        internal void Reselect() => OnPropertyChanged(nameof(Monitor));

        private string _status = "";
        public string Status { get => _status; set => Set(ref _status, value); }
    }

    /// <summary>
    /// The TV outputs: each zone's TV picture full screen on a monitor of this PC (the TVs above the zones), chosen on the
    /// TV Ekranı page and remembered (TvDisplays.xml), so the pictures come back on the same monitors at the next start.
    /// </summary>
    public sealed class TvOutputs
    {
        private readonly HmiContext _ctx;
        private readonly Dictionary<int, TvWindow> _windows = new Dictionary<int, TvWindow>();

        /// <summary>Where the assignments are kept; the smoke test points this at its own folder.</summary>
        public static string SettingsFile { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SempaPumpStation", "TvDisplays.xml");

        public ObservableCollection<MonitorChoice> Monitors { get; } = new ObservableCollection<MonitorChoice>();
        public ObservableCollection<TvOutputRow> Rows { get; } = new ObservableCollection<TvOutputRow>();
        public ICommand RefreshCommand { get; }
        public ICommand IdentifyCommand { get; }

        public TvOutputs(HmiContext ctx)
        {
            _ctx = ctx;
            for (int z = 1; z <= Station.ZoneCount; z++) Rows.Add(new TvOutputRow(this, z));
            RefreshCommand = new SyncCommand(RefreshMonitors);
            IdentifyCommand = new SyncCommand(Identify);
            Load();
            RefreshMonitors();   // after Load: a saved monitor that is not connected stays in the list
        }

        /// <summary>The zone's TV window, if it is shown.</summary>
        public TvWindow Window(int zone) => _windows.TryGetValue(zone, out var w) ? w : null;

        /// <summary>The monitor list is being rebuilt: the combo boxes' passing changes are not assignments.</summary>
        internal bool Refreshing { get; private set; }

        /// <summary>
        /// Lists the monitors again (after one was plugged in or out) and updates the rows' state. A saved monitor that is not
        /// connected stays in the list, so its assignment is kept and comes back when it is connected again.
        /// </summary>
        public void RefreshMonitors()
        {
            Refreshing = true;
            try
            {
                Monitors.Clear();
                Monitors.Add(new MonitorChoice { DeviceName = "", Display = "Gösterilmiyor" });
                var present = DisplayMonitors.All();
                foreach (var m in present) Monitors.Add(new MonitorChoice { DeviceName = m.DeviceName, Display = m.Display });
                foreach (var r in Rows.Where(r => r.Monitor != "" && !present.Any(m => string.Equals(m.DeviceName, r.Monitor, StringComparison.OrdinalIgnoreCase))))
                    Monitors.Add(new MonitorChoice { DeviceName = r.Monitor, Display = r.Monitor.TrimStart('\\', '.') + " (bağlı değil)" });
            }
            finally { Refreshing = false; }
            foreach (var r in Rows) { r.Reselect(); UpdateStatus(r); }
        }

        /// <summary>Opens the saved assignments (at start-up); a monitor that is not there is left out.</summary>
        public void OpenSaved()
        {
            foreach (var r in Rows)
                if (r.Monitor != "") Open(r);
        }

        /// <summary>Shows the zone's TV picture on the monitor ("" = nowhere). A monitor shows one zone: another zone on it is taken off.</summary>
        public void Assign(int zone, string device)
        {
            var row = Rows.First(r => r.Zone == zone);
            if (device != "")
                foreach (var other in Rows.Where(r => r != row && string.Equals(r.Monitor, device, StringComparison.OrdinalIgnoreCase)))
                {
                    Close(other.Zone);
                    other.SetMonitor("");
                    UpdateStatus(other);
                }

            Close(zone);
            row.SetMonitor(device);
            if (device != "") Open(row);
            UpdateStatus(row);
            Save();
        }

        public void CloseAll()
        {
            foreach (int zone in _windows.Keys.ToList()) Close(zone);
        }

        private void Open(TvOutputRow row)
        {
            var monitor = DisplayMonitors.Find(row.Monitor);
            if (monitor == null) { UpdateStatus(row); return; }
            var window = new TvWindow(new TvViewModel(_ctx, row.Zone, output: true), monitor);
            window.Closed += (s, e) =>
            {
                if (_windows.TryGetValue(row.Zone, out var w) && w == window) _windows.Remove(row.Zone);
                UpdateStatus(row);
            };
            _windows[row.Zone] = window;
            window.Show();
            UpdateStatus(row);
        }

        private void Close(int zone)
        {
            if (!_windows.TryGetValue(zone, out var w)) return;
            _windows.Remove(zone);
            w.Close();
        }

        private void UpdateStatus(TvOutputRow row)
        {
            var monitor = DisplayMonitors.Find(row.Monitor);
            row.Status = row.Monitor == "" ? ""
                       : monitor == null ? "Ekran bağlı değil"
                       : _windows.ContainsKey(row.Zone) ? "Ekran " + monitor.Number + "'de gösteriliyor"
                       : "Kapalı";
        }

        /// <summary>Shows each monitor's number on it for a few seconds, to tell them apart.</summary>
        private static void Identify()
        {
            foreach (var m in DisplayMonitors.All())
            {
                var w = new IdentifyWindow(m);
                w.Show();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, e) => { timer.Stop(); w.Close(); };
                timer.Start();
            }
        }

        // ---------------------------------------------------------------- settings
        private void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (var e in XDocument.Load(SettingsFile).Root.Elements("Zone"))
                {
                    var row = Rows.FirstOrDefault(r => r.Zone == (int?)e.Attribute("Number"));
                    if (row != null) row.SetMonitor((string)e.Attribute("Monitor") ?? "");
                }
                foreach (var r in Rows) UpdateStatus(r);
            }
            catch (Exception) { /* a damaged file only loses the assignments */ }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                new XDocument(new XElement("TvDisplays",
                        Rows.Select(r => new XElement("Zone", new XAttribute("Number", r.Zone), new XAttribute("Monitor", r.Monitor)))))
                    .Save(SettingsFile);
            }
            catch (Exception) { /* not saved: the assignment holds until the HMI closes */ }
        }
    }

    /// <summary>A zone's TV picture, borderless over a whole monitor, without a mouse pointer.</summary>
    public sealed class TvWindow : Window
    {
        public TvViewModel ViewModel { get; }
        public DisplayMonitor Monitor { get; }

        public TvWindow(TvViewModel vm, DisplayMonitor monitor)
        {
            ViewModel = vm;
            Monitor = monitor;
            Title = AppVersion.ProductName + " TV — Bölge " + vm.Zone;
            Icon = AppIcon.Image;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            ShowActivated = false;
            Cursor = Cursors.None;
            Background = (Brush)Application.Current.FindResource("B.App");
            Content = new TvView { DataContext = vm };
            SourceInitialized += (s, e) => DisplayMonitors.FillMonitor(this, monitor);
            Loaded += (s, e) => WindowState = WindowState.Maximized;   // maximised on the monitor it was placed on
            Closed += (s, e) => vm.Detach();
        }
    }

    /// <summary>A monitor's number, shown on it for a moment.</summary>
    internal sealed class IdentifyWindow : Window
    {
        public IdentifyWindow(DisplayMonitor monitor)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = (Brush)Application.Current.FindResource("B.App");
            Content = new Border
            {
                BorderBrush = (Brush)Application.Current.FindResource("B.Accent"),
                BorderThickness = new Thickness(8),
                Child = new TextBlock
                {
                    Text = "EKRAN " + monitor.Number,
                    FontSize = 160,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("B.Accent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            SourceInitialized += (s, e) => DisplayMonitors.FillMonitor(this, monitor);
        }
    }
}
