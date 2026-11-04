using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// Operation screen of one test zone: power source / flowmeter / transmitter selection, speed
    /// reference, test start and end, the sample buffer, and the simulated field (pendant, E-stop,
    /// discharge valve, injected faults). Writes go to StationHp; everything shown comes from StationPh.
    /// </summary>
    public sealed class ZoneViewModel : ScreenBase
    {
        private readonly int _zone;
        private int _lastCount = -1;
        private string _lastStarted;

        public int Zone => _zone;
        public override string Key => "Zone" + _zone;
        public override string Title => "Bölge " + _zone;

        public TagScope Hp { get; }
        public TagScope Ph { get; }
        public TagScope Samples { get; }
        public TagScope Sim { get; }
        public TagScope Ai { get; }

        public ObservableCollection<ChoiceItem> Sources { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<ChoiceItem> Flowmeters { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<ChoiceItem> Transmitters { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<TestSample> SampleRows { get; } = new ObservableCollection<TestSample>();

        public ICommand ClickCommand { get; }
        public ICommand PendantCommand { get; }
        public ICommand ToggleEStopCommand { get; }
        public ICommand ToggleFeedbackFaultCommand { get; }
        public ICommand ToggleDriveFaultCommand { get; }

        public ZoneViewModel(HmiContext ctx, int zone) : base(ctx)
        {
            _zone = zone;
            Hp = ctx.Scope(Station.ZoneHp(zone));
            Ph = ctx.Scope(Station.ZonePh(zone));
            Samples = ctx.Scope(Station.Samples(zone));
            Sim = ctx.Scope(Station.SimZone(zone));
            Ai = ctx.Scope("MaintenancePh.Io.AnalogInputs.Zone" + zone + "_");

            Sources.Add(Choice(0, "Kaynak yok", "PowerSource"));
            foreach (var s in Station.Sources) Sources.Add(Choice(s.Id, s.Name, "PowerSource"));
            for (int f = 1; f <= Station.FlowmeterCount(zone); f++) Flowmeters.Add(Choice(f, "FM" + f, "Flowmeter"));
            for (int t = 1; t <= Station.TransmitterCount; t++) Transmitters.Add(Choice(t, "Pt" + t, "DischargeTransmitter"));

            ClickCommand = new SyncCommand(p => Ctx.Click(Hp.Prefix + p));
            PendantCommand = new SyncCommand(p => Ctx.Pulse(Sim.Prefix + p));
            ToggleEStopCommand = new SyncCommand(() => Ctx.Toggle(Sim.Prefix + "EStopPressed"));
            ToggleFeedbackFaultCommand = new SyncCommand(() => Ctx.Toggle(Sim.Prefix + "FeedbackFault"));
            ToggleDriveFaultCommand = new SyncCommand(() =>
            {
                int s = SelectedSource;
                if (s > 0) Ctx.Toggle(Station.SimDrive(s) + "Fault");
            });

            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private ChoiceItem Choice(int value, string title, string parameter) => new ChoiceItem
        {
            Value = value,
            Title = title,
            Command = new SyncCommand(() => Ctx.SetValue(Hp.Prefix + parameter + ".Value", (short)value)),
        };

        // ---------------------------------------------------------------- live state
        public int SelectedSource => Ctx.GetInt(Ph.Prefix + "PowerSource.Value");
        public bool SelectionEnabled => Ctx.GetBool(Ph.Prefix + "PowerSource.Enabled");

        private string _powerStateText;
        public string PowerStateText { get => _powerStateText; private set => Set(ref _powerStateText, value); }

        private string _sourceText;
        public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }

        private bool _isVfd;
        public bool IsVfd { get => _isVfd; private set => Set(ref _isVfd, value); }

        private double _head;
        /// <summary>Live total head, m (pressure difference + gauge height difference).</summary>
        public double Head { get => _head; private set => Set(ref _head, value); }

        private string _flowUnit = "m³/h";
        public string FlowUnit { get => _flowUnit; private set => Set(ref _flowUnit, value); }

        private string _instrumentsText;
        public string InstrumentsText { get => _instrumentsText; private set => Set(ref _instrumentsText, value); }

        private string _testText;
        public string TestText { get => _testText; private set => Set(ref _testText, value); }

        private bool _driveFaultInjected;
        public bool DriveFaultInjected { get => _driveFaultInjected; private set => Set(ref _driveFaultInjected, value); }

        public double ValveOpening
        {
            get => Ctx.GetDouble(Sim.Prefix + "ValveOpening");
            set { Ctx.SetValue(Sim.Prefix + "ValveOpening", (float)value); OnPropertyChanged(); }
        }

        private void Refresh()
        {
            int source = SelectedSource;
            var info = Station.Source(source);
            PowerStateText = Station.PowerStateText(Ctx.GetInt(Ph.Prefix + "PowerState"));
            SourceText = info?.Name ?? "Kaynak seçilmedi";
            IsVfd = info != null && info.IsVfd;
            DriveFaultInjected = source > 0 && Ctx.GetBool(Station.SimDrive(source) + "Fault");

            // Sources: selected here, or held by another zone (selected there or its device there not open)
            foreach (var c in Sources)
            {
                int other = c.Value > 0 ? OtherZoneWithSource(c.Value) : 0;
                c.State = c.Value == source ? "active" : other > 0 ? "held" : "";
                var s = Station.Source(c.Value);
                c.Subtitle = c.Value == 0 ? "Tüm cihazlar açık"
                           : other > 0 ? "Bölge " + other + " kullanıyor"
                           : s.IsMagneticSwitch ? "Manyetik şalter" : "Kontaktör";
            }

            int flowmeter = Ctx.GetInt(Ph.Prefix + "Flowmeter.Value");
            var fmPositions = Station.FlowmeterPositions(_zone);
            foreach (var c in Flowmeters)
            {
                int device = Ctx.GetInt(Station.PositionPh(fmPositions[c.Value - 1]) + "Device.Value");
                bool held = _zone <= 2 && Ctx.GetInt(Station.ZonePh(3 - _zone) + "Flowmeter.Value") == c.Value;
                c.State = c.Value == flowmeter ? "active" : held ? "held" : "";
                c.Subtitle = held ? "Bölge " + (3 - _zone) + " kullanıyor" : DeviceText(Station.Flowmeter, device);
            }

            int transmitter = Ctx.GetInt(Ph.Prefix + "DischargeTransmitter.Value");
            foreach (var c in Transmitters)
            {
                int device = Ctx.GetInt(Station.PositionPh(Station.TransmitterPosition(_zone, c.Value)) + "Device.Value");
                c.State = c.Value == transmitter ? "active" : "";
                c.Subtitle = DeviceText(Station.PressureTransmitter, device);
            }

            int fmDevice = Ctx.GetInt(Ph.Prefix + "FlowmeterDevice");
            string unit = fmDevice > 0 ? Ctx.GetString(Station.LibraryEntry(Station.Flowmeter, fmDevice) + "Unit") : "";
            FlowUnit = string.IsNullOrEmpty(unit) ? "m³/h" : unit;
            InstrumentsText = "Vakummetre: " + DeviceText(Station.Vacuummeter, Ctx.GetInt(Ph.Prefix + "VacuummeterDevice"));

            double pd = Ctx.GetDouble(Ph.Prefix + "DischargePressure"), ps = Ctx.GetDouble(Ph.Prefix + "SuctionPressure");
            bool submersible = Ctx.GetBool("DALGIC_PMP_SEL");
            Head = (pd - (submersible ? 0 : ps)) * 10.197 + Ctx.GetDouble("GEO_YUKSEKLIK");

            bool active = Ctx.GetBool(Samples.Prefix + "TestActive");
            string started = Ctx.GetDtl(Samples.Prefix + "TestStarted");
            string ended = Ctx.GetDtl(Samples.Prefix + "TestEnded");
            TestText = active ? "Test sürüyor — başlangıç " + started
                     : string.IsNullOrEmpty(started) ? "Test başlatılmadı"
                     : "Son test " + started + " → " + ended;

            RefreshSamples(started);
        }

        private int OtherZoneWithSource(int source)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                if (z == _zone) continue;
                if (Ctx.GetInt(Station.ZonePh(z) + "PowerSource.Value") == source) return z;
            }
            // A device still closed in another zone holds the source too
            int fed = Ctx.FedZone(source);
            return fed > 0 && fed != _zone ? fed : 0;
        }

        private string DeviceText(int category, int device)
        {
            if (device <= 0) return "Cihaz takılı değil";
            string d = Ctx.GetString(Station.LibraryEntry(category, device) + "Description");
            return "#" + device + " " + (string.IsNullOrEmpty(d) ? "(tanımsız)" : d);
        }

        private void RefreshSamples(string started)
        {
            int count = Ctx.GetInt(Samples.Prefix + "Count");
            if (count == _lastCount && started == _lastStarted) return;
            _lastCount = count;
            _lastStarted = started;

            var record = new TestRecord { Zone = _zone };
            record.Header["GEO_YUKSEKLIK"] = Ctx.GetDouble("GEO_YUKSEKLIK").ToString(System.Globalization.CultureInfo.InvariantCulture);
            record.Header["DALGIC_PMP_SEL"] = Ctx.GetBool("DALGIC_PMP_SEL").ToString();
            for (int i = 1; i <= count; i++) record.Samples.Add(ReadSample(i));
            TestArchive.Derive(record);

            SampleRows.Clear();
            foreach (var s in record.Samples.AsEnumerable().Reverse()) SampleRows.Add(s);
        }

        private TestSample ReadSample(int i)
        {
            string p = Station.Sample(_zone, i);
            return new TestSample
            {
                No = i,
                Timestamp = Ctx.GetDtl(p + "Timestamp"),
                PowerSource = Ctx.GetInt(p + "PowerSource"),
                Flowmeter = Ctx.GetInt(p + "Flowmeter"),
                DischargeTransmitter = Ctx.GetInt(p + "DischargeTransmitter"),
                Flow = Ctx.GetDouble(p + "Flow"),
                DischargePressure = Ctx.GetDouble(p + "DischargePressure"),
                SuctionPressure = Ctx.GetDouble(p + "SuctionPressure"),
                Speed = Ctx.GetDouble(p + "Speed"),
                SpeedReference = Ctx.GetDouble(p + "SpeedReference"),
                WaterTemperature = Ctx.GetDouble(p + "WaterTemperature"),
                Vibration = Ctx.GetDouble(p + "Vibration"),
                Temperature = Ctx.GetDouble(p + "Temperature"),
                PumpRunning = Ctx.GetBool(p + "PumpRunning"),
            };
        }
    }
}
