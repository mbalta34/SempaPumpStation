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
        public ObservableCollection<ChoiceItem> Vacuummeters { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<TestSample> SampleRows { get; } = new ObservableCollection<TestSample>();
        /// <summary>What the pump efficiency is calculated from: electrical, torque or both (kept per zone on this PC).</summary>
        public ObservableCollection<ChoiceItem> EfficiencyBases { get; } = new ObservableCollection<ChoiceItem>();

        public ICommand ClickCommand { get; }
        public ICommand PendantCommand { get; }
        public ICommand ToggleEStopCommand { get; }
        public ICommand TogglePanelEStopCommand { get; }
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
            for (int t = 1; t <= Station.TransmitterCount(zone); t++) Transmitters.Add(Choice(t, "Pt" + t, "DischargeTransmitter"));
            for (int v = 1; v <= Station.VacuummeterCount(zone); v++) Vacuummeters.Add(Choice(v, "Vakum" + v, "Vacuummeter"));
            foreach (EfficiencyBasis b in new[] { EfficiencyBasis.Electrical, EfficiencyBasis.Torque, EfficiencyBasis.Both })
            {
                var basis = b;
                EfficiencyBases.Add(new ChoiceItem
                {
                    Value = (int)b,
                    Title = EfficiencyBasisStore.Text(b),
                    Subtitle = b == EfficiencyBasis.Electrical ? "Nş × motor verimi" : b == EfficiencyBasis.Torque ? "Tork × devir / 9549" : "İkisi yan yana",
                    Command = new SyncCommand(() => { EfficiencyBasisStore.Set(_zone, basis); Refresh(); }),
                });
            }

            ClickCommand = new SyncCommand(p => Ctx.Click(Hp.Prefix + p));
            PendantCommand = new SyncCommand(p => Ctx.Pulse(Sim.Prefix + p));
            ToggleEStopCommand = new SyncCommand(() => Ctx.Toggle(Sim.Prefix + "EStopPressed"));
            TogglePanelEStopCommand = new SyncCommand(() => Ctx.Toggle(Sim.Prefix + "PanelEStopPressed"));
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
            Command = new SyncCommand(() => Ctx.WriteParameter(Hp.Prefix + parameter + ".Value", Ph.Prefix + parameter + ".Value", (short)value)),
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

        // From the PM5550 of the selected source; "—" while none is selected or its meter does not answer
        private string _powerText = "—", _efficiencyText = "—", _voltageText = "—", _currentText = "—";
        public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
        public string EfficiencyText { get => _efficiencyText; private set => Set(ref _efficiencyText, value); }
        public string VoltageText { get => _voltageText; private set => Set(ref _voltageText, value); }
        public string CurrentText { get => _currentText; private set => Set(ref _currentText, value); }
        private string _powerFactorText = "—";
        /// <summary>cos φ of the selected source (on the 630 kW VFD the drive's motor side).</summary>
        public string PowerFactorText { get => _powerFactorText; private set => Set(ref _powerFactorText, value); }
        private string _dischargeHeadText = "—", _suctionHeadText = "—";
        /// <summary>Hb and He, the discharge and suction heads in m (as the report).</summary>
        public string DischargeHeadText { get => _dischargeHeadText; private set => Set(ref _dischargeHeadText, value); }
        public string SuctionHeadText { get => _suctionHeadText; private set => Set(ref _suctionHeadText, value); }

        private bool _showElectrical = true, _showTorque;
        /// <summary>The electrical values (U, I, cos φ, Nş, Pp and the efficiency on Pp) are shown: basis Elektrik or both.</summary>
        public bool ShowElectrical { get => _showElectrical; private set => Set(ref _showElectrical, value); }
        /// <summary>The torque values (torque, Nmil and the efficiency on Nmil) are shown: basis Tork or both.</summary>
        public bool ShowTorque { get => _showTorque; private set => Set(ref _showTorque, value); }

        private string _shaftPowerText = "—", _shaftEfficiencyText = "—";
        /// <summary>Nmil, the shaft power from the torque meter (T × n / 9549), and the pump efficiency on it.</summary>
        public string ShaftPowerText { get => _shaftPowerText; private set => Set(ref _shaftPowerText, value); }
        public string ShaftEfficiencyText { get => _shaftEfficiencyText; private set => Set(ref _shaftEfficiencyText, value); }

        private string _electricalPowerText = "—";
        /// <summary>Nş, the electrical input from the meter values (√3·U·I·cosφ).</summary>
        public string ElectricalPowerText { get => _electricalPowerText; private set => Set(ref _electricalPowerText, value); }

        private string _flowUnit = "m³/h";
        public string FlowUnit { get => _flowUnit; private set => Set(ref _flowUnit, value); }

        private string _instrumentsText;
        public string InstrumentsText { get => _instrumentsText; private set => Set(ref _instrumentsText, value); }

        // A contactor or magnetic switch of some zone was manipulated: every motor is stopped until that zone is reset
        private string _safetyStopText = "";
        public string SafetyStopText { get => _safetyStopText; private set => Set(ref _safetyStopText, value); }

        private string _testText;
        public string TestText { get => _testText; private set => Set(ref _testText, value); }

        // Rotation test: Station.Rotation* state and a line for the operator
        private int _rotationState;
        private string _rotationText = "";
        public int RotationState { get => _rotationState; private set => Set(ref _rotationState, value); }
        public string RotationText { get => _rotationText; private set => Set(ref _rotationText, value); }

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
                // Every zone has its own flowmeters
                c.State = c.Value == flowmeter ? "active" : "";
                c.Subtitle = DeviceText(Station.Flowmeter, device);
            }

            int transmitter = Ctx.GetInt(Ph.Prefix + "DischargeTransmitter.Value");
            foreach (var c in Transmitters)
            {
                int device = Ctx.GetInt(Station.PositionPh(Station.TransmitterPosition(_zone, c.Value)) + "Device.Value");
                c.State = c.Value == transmitter ? "active" : "";
                c.Subtitle = DeviceText(Station.PressureTransmitter, device);
            }

            int vacuummeter = Ctx.GetInt(Ph.Prefix + "Vacuummeter.Value");
            foreach (var c in Vacuummeters)
            {
                int device = Ctx.GetInt(Station.PositionPh(Station.VacuummeterPosition(_zone, c.Value)) + "Device.Value");
                c.State = c.Value == vacuummeter ? "active" : "";
                c.Subtitle = DeviceText(Station.Vacuummeter, device);
            }

            int fmDevice = Ctx.GetInt(Ph.Prefix + "FlowmeterDevice");
            string unit = fmDevice > 0 ? Ctx.GetString(Station.LibraryEntry(Station.Flowmeter, fmDevice) + "Unit") : "";
            FlowUnit = string.IsNullOrEmpty(unit) ? "m³/h" : unit;
            InstrumentsText = "Emme: " + (vacuummeter > 0 ? DeviceText(Station.Vacuummeter, Ctx.GetInt(Ph.Prefix + "VacuummeterDevice")) : "vakummetre seçilmedi");

            var reading = PumpCalc.Live(Ctx, _zone);
            Head = reading.Head;
            PowerText = reading.PumpPower?.ToString("0.00") ?? "—";
            ElectricalPowerText = reading.ElectricalPower?.ToString("0.00") ?? "—";
            DischargeHeadText = reading.DischargeHead.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);   // as Hm and the pressures
            SuctionHeadText = reading.SuctionHead.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            EfficiencyText = reading.Efficiency?.ToString("0.0") ?? "—";
            VoltageText = reading.Source > 0 ? reading.Voltage.ToString("0") : "—";
            CurrentText = reading.Source > 0 ? reading.Current.ToString("0.0") : "—";
            PowerFactorText = reading.Source > 0 ? reading.PowerFactor.ToString("0.00") : "—";
            ShaftPowerText = reading.ShaftPower?.ToString("0.00") ?? "—";
            ShaftEfficiencyText = reading.ShaftEfficiency?.ToString("0.0") ?? "—";

            var basisNow = EfficiencyBasisStore.Get(_zone);
            ShowElectrical = EfficiencyBasisStore.ShowsElectrical(basisNow);
            ShowTorque = EfficiencyBasisStore.ShowsTorque(basisNow);
            foreach (var c in EfficiencyBases) c.State = c.Value == (int)basisNow ? "active" : "";

            bool active = Ctx.GetBool(Samples.Prefix + "TestActive");
            string started = Ctx.GetDtl(Samples.Prefix + "TestStarted");
            string ended = Ctx.GetDtl(Samples.Prefix + "TestEnded");
            int testNo = Ctx.GetInt(Samples.Prefix + "TestNo");
            TestText = active ? "Deney " + testNo + " sürüyor — başlangıç " + started
                     : string.IsNullOrEmpty(started) ? "Test başlatılmadı"
                     : "Son test (Deney " + testNo + ") " + started + " → " + ended;

            string manipulated = "";
            for (int z = 1; z <= Station.ZoneCount && manipulated == ""; z++)
            {
                int device = Ctx.GetInt(Station.ZonePh(z) + "ManipulatedDevice");
                if (device > 0) manipulated = "Bölge " + z + ", " + Station.DeviceText(device);
            }
            SafetyStopText = "MÜDAHALE" + (manipulated == "" ? "" : " (" + manipulated + ")")
                           + " — tüm motorlar durduruldu; cihazı yerine getirip o bölgede Arıza Reset'e basın";

            RotationState = Ctx.GetInt(Ph.Prefix + "RotationState");
            RotationText = RotationState == Station.RotationRunning
                ? Station.RotationStateText(RotationState) + " — " + Ctx.GetDouble(Ph.Prefix + "RotationRemaining").ToString("0.0") + " sn"
                : Station.RotationStateText(RotationState);

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
            // The loaded recipes, as the archive takes them at test end, so Hm here matches the live tiles and the report
            foreach (var k in TestArchive.HeaderKeys)
                record.Header[k] = Convert.ToString(Ctx.Tag(k)?.TagValue, System.Globalization.CultureInfo.InvariantCulture) ?? "";
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
                Vacuummeter = Ctx.GetInt(p + "Vacuummeter"),
                Flow = Ctx.GetDouble(p + "Flow"),
                DischargePressure = Ctx.GetDouble(p + "DischargePressure"),
                SuctionPressure = Ctx.GetDouble(p + "SuctionPressure"),
                Speed = Ctx.GetDouble(p + "Speed"),
                SpeedReference = Ctx.GetDouble(p + "SpeedReference"),
                WaterTemperature = Ctx.GetDouble(p + "WaterTemperature"),
                Vibration = Ctx.GetDouble(p + "Vibration"),
                Temperature = Ctx.GetDouble(p + "Temperature"),
                PumpRunning = Ctx.GetBool(p + "PumpRunning"),
                Torque = Ctx.GetDouble(p + "Torque"),
                PowerFactor = Ctx.GetDouble(p + "PowerFactor"),
            };
        }
    }
}
