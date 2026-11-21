using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace SempaPumpStation.Infrastructure
{
    public sealed class AlarmDef
    {
        public string Tag;
        public string Message;
        public string Severity;      // "ALARM" | "WARN"
        public bool ActiveWhenFalse; // e.g. PLC_COMM_OK is an alarm when false
        public int? ActiveWhenEquals; // an Int tag that is an alarm while it holds this value
    }

    public sealed class AlarmRow : INotifyPropertyChanged
    {
        public DateTime Time { get; set; }
        public string TimeText => Time.ToString("dd.MM.yyyy HH:mm:ss");
        public string Message { get; set; }
        public string Severity { get; set; }
        public string State { get; set; }     // "AKTİF" | "NORMAL" | "OLAY"
        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Scans the alarm/event bits each second, maintains the live active-alarm set, and
    /// appends a timestamped history (the Winlog online-alarm + historical-event equivalent).</summary>
    public sealed class AlarmMonitor : INotifyPropertyChanged
    {
        private readonly HmiContext _ctx;
        private readonly DispatcherTimer _timer;
        private readonly List<AlarmDef> _defs;
        private readonly Dictionary<AlarmDef, AlarmRow> _activeMap = new Dictionary<AlarmDef, AlarmRow>();

        public ObservableCollection<AlarmRow> Active { get; } = new ObservableCollection<AlarmRow>();
        public ObservableCollection<AlarmRow> History { get; } = new ObservableCollection<AlarmRow>();

        public int ActiveCount => Active.Count;
        public bool HasAlarm => Active.Count > 0;
        public string Banner => Active.Count > 0 ? Active[0].Message : "Sistem normal — aktif alarm yok";
        public string BannerSeverity => Active.Count > 0 ? Active[0].Severity : "OK";

        public AlarmMonitor(HmiContext ctx)
        {
            _ctx = ctx;
            _defs = Catalog();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += (s, e) => Scan();
        }

        public void Start() { Scan(); _timer.Start(); }
        public void Stop() => _timer.Stop();

        public void LogEvent(string message)
        {
            History.Insert(0, new AlarmRow { Time = DateTime.Now, Message = message, Severity = "OLAY", State = "OLAY" });
            Trim();
        }

        private void Scan()
        {
            bool changed = false;
            foreach (var d in _defs)
            {
                bool raw = d.ActiveWhenEquals.HasValue ? _ctx.GetInt(d.Tag) == d.ActiveWhenEquals.Value : _ctx.GetBool(d.Tag);
                bool active = d.ActiveWhenFalse ? !raw : raw;
                bool wasActive = _activeMap.ContainsKey(d);

                if (active && !wasActive)
                {
                    var row = new AlarmRow { Time = DateTime.Now, Message = d.Message, Severity = d.Severity, State = "AKTİF" };
                    _activeMap[d] = row;
                    Active.Insert(0, row);
                    History.Insert(0, new AlarmRow { Time = row.Time, Message = d.Message, Severity = d.Severity, State = "AKTİF" });
                    changed = true;
                }
                else if (!active && wasActive)
                {
                    Active.Remove(_activeMap[d]);
                    _activeMap.Remove(d);
                    History.Insert(0, new AlarmRow { Time = DateTime.Now, Message = d.Message, Severity = d.Severity, State = "NORMAL" });
                    changed = true;
                }
            }
            if (changed) { Trim(); Raise(); }
        }

        private void Trim() { while (History.Count > 500) History.RemoveAt(History.Count - 1); }

        private void Raise()
        {
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(HasAlarm));
            OnPropertyChanged(nameof(Banner));
            OnPropertyChanged(nameof(BannerSeverity));
        }

        /// <summary>Alarm Reset: the zones' ResetFault buttons (E-stop, device and drive faults).</summary>
        public void ResetAll()
        {
            for (int z = 1; z <= Station.ZoneCount; z++) _ctx.Click(Station.ZoneHp(z) + "ResetFault");
            LogEvent("Operatör: Alarm reset");
        }

        private static List<AlarmDef> Catalog()
        {
            var list = new List<AlarmDef>
            {
                new AlarmDef { Tag = "PLC_COMM_OK", Message = "PLC ile SCADA haberleşmesi koptu", Severity = "ALARM", ActiveWhenFalse = true },
                new AlarmDef { Tag = "PLC_INTERFACE_OK", Message = "PLC programı bu HMI sürümüyle uyumsuz — güncel programı PLC'ye yükleyin", Severity = "ALARM", ActiveWhenFalse = true },
                new AlarmDef { Tag = "PLC_VERSION_OK", Message = "PLC program sürümü bu HMI'nın beklediğinden farklı (HMI PLC " + AppVersion.ExpectedPlcVersion + " bekliyor)", Severity = "WARN", ActiveWhenFalse = true },
            };
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string ph = Station.ZonePh(z), name = "Bölge " + z + " — ";
                list.Add(new AlarmDef { Tag = ph + "EStop", Message = name + "ACİL STOP", Severity = "ALARM" });
                list.Add(new AlarmDef { Tag = ph + "DeviceFault", Message = name + "Kontaktör / şalter arızası (geri bildirim yok)", Severity = "ALARM" });
                list.Add(new AlarmDef { Tag = ph + "DriveFault", Message = name + "Sürücü arızası", Severity = "ALARM" });
                // A device that changed state without a command stops every motor of the station (PLC, FB_Station_Zone)
                foreach (var s in Station.Sources)
                    list.Add(new AlarmDef
                    {
                        Tag = ph + "ManipulatedDevice", ActiveWhenEquals = s.Id, Severity = "ALARM",
                        Message = name + Station.DeviceText(s.Id) + " komutsuz konum değiştirdi (müdahale) — tüm motorlar durduruldu",
                    });
                list.Add(new AlarmDef { Tag = ph + "SampleBufferFull", Message = name + "Numune hafızası dolu (50)", Severity = "WARN" });
            }
            // A forced input or output bypasses the station program's interlocks: a warning for as long as it is forced
            foreach (var i in DigitalInputs.All)
                list.Add(new AlarmDef
                {
                    Tag = i.Ph + "ForcingActive",
                    Message = (i.Zone > 0 ? "Bölge " + i.Zone : "Sürücü") + " — Giriş zorlanıyor: " + i.Label,
                    Severity = "WARN",
                });
            foreach (var o in DigitalOutputs.All)
                list.Add(new AlarmDef
                {
                    Tag = o.Ph + "ForcingActive",
                    Message = (o.Zone > 0 ? "Bölge " + o.Zone : "Sürücü") + " — Çıkış zorlanıyor: " + o.Label,
                    Severity = "WARN",
                });
            // SelectionRejected is not an alarm: the PLC keeps it set until the next accepted selection
            return list;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
