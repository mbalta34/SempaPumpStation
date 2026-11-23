using System;
using System.Collections.ObjectModel;
using System.Globalization;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>One value tile of the TV screen: caption, value and unit; <see cref="Kind"/> colours the value.</summary>
    public sealed class TvTile : ViewModelBase
    {
        public string Caption { get; set; }
        private string _unit;
        public string Unit { get => _unit; set => Set(ref _unit, value); }
        /// <summary>"run" (green), "accent" (blue) or "" (text colour).</summary>
        public string Kind { get; set; } = "";

        private string _value = "—";
        public string Value { get => _value; set => Set(ref _value, value); }
    }

    /// <summary>
    /// The big display above a test zone (the old "TV EKRANI" page), in the application's theme: the zone, its source and
    /// state, the sample (KAYIT) and test (DENEY) numbers, Q, Hm, pump power and efficiency as large tiles, and every other
    /// value of the zone screen as smaller tiles, readable from across the hall.
    /// </summary>
    public sealed class TvViewModel : ScreenBase
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public override string Key => "Tv";
        public override string Title => "TV Ekranı";

        public ObservableCollection<ChoiceItem> Zones { get; } = new ObservableCollection<ChoiceItem>();
        /// <summary>Q, Hm, Pp and efficiency.</summary>
        public ObservableCollection<TvTile> MainTiles { get; } = new ObservableCollection<TvTile>();
        /// <summary>Every other value of the zone screen.</summary>
        public ObservableCollection<TvTile> Tiles { get; } = new ObservableCollection<TvTile>();

        private readonly TvTile _flow, _head, _power, _efficiency;
        private readonly TvTile _discharge, _suction, _dischargeHead, _suctionHead, _electrical, _voltage, _current,
                                _speed, _torque, _water, _motor, _ambient, _vibration, _sound;

        private int _zone = 1;
        public int Zone
        {
            get => _zone;
            set
            {
                if (!Set(ref _zone, value)) return;
                foreach (var z in Zones) z.State = z.Value == value ? "active" : "";
                Refresh();
            }
        }

        private string _sourceText, _recordNo, _testNo, _statusText, _statusKind, _clock;
        public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }
        public string RecordNo { get => _recordNo; private set => Set(ref _recordNo, value); }
        public string TestNo { get => _testNo; private set => Set(ref _testNo, value); }
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        /// <summary>"alarm", "run", "test" or "idle": the colour of the state banner.</summary>
        public string StatusKind { get => _statusKind; private set => Set(ref _statusKind, value); }
        public string Clock { get => _clock; private set => Set(ref _clock, value); }

        /// <summary>The TV Ekranı page: a preview with a zone picker, and the TV outputs.</summary>
        public TvViewModel(HmiContext ctx) : this(ctx, 1, false) { }

        /// <param name="output">A TV window's picture: the zone is fixed and the page's controls are left out.</param>
        public TvViewModel(HmiContext ctx, int zone, bool output) : base(ctx)
        {
            _zone = zone;
            IsOutput = output;
            if (!output) Outputs = new TvOutputs(ctx);
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                int choice = z;
                Zones.Add(new ChoiceItem { Value = z, Title = "Bölge " + z, State = z == zone ? "active" : "", Command = new SyncCommand(() => Zone = choice) });
            }

            TvTile Main(string caption, string unit, string kind = "") { var t = new TvTile { Caption = caption, Unit = unit, Kind = kind }; MainTiles.Add(t); return t; }
            TvTile Small(string caption, string unit, string kind = "") { var t = new TvTile { Caption = caption, Unit = unit, Kind = kind }; Tiles.Add(t); return t; }

            _flow = Main("DEBİ (Q)", "m³/h", "run");
            _head = Main("BASMA YÜKSEKLİĞİ (Hm)", "m", "accent");
            _power = Main("POMPA GÜCÜ (Pp)", "kW", "run");
            _efficiency = Main("VERİM", "%", "accent");

            _discharge = Small("BASMA BASINCI", "bar");
            _suction = Small("EMME BASINCI", "bar");
            _dischargeHead = Small("Hb", "m");
            _suctionHead = Small("He", "m");
            _electrical = Small("ELEKTRİK GÜCÜ (Nş)", "kW");
            _voltage = Small("GERİLİM (F-F)", "V");
            _current = Small("AKIM", "A");
            _speed = Small("DEVİR", "d/dk");
            _torque = Small("TORK", "Nm");
            _water = Small("SU SICAKLIĞI", "°C");
            _motor = Small("MOTOR SICAKLIĞI", "°C");
            _ambient = Small("ORTAM SICAKLIĞI", "°C");
            _vibration = Small("TİTREŞİM", "mm/s");
            _sound = Small("SES", "dB");

            ctx.Scanned += OnScanned;
            Refresh();
        }

        /// <summary>A TV window's picture (no zone picker, no outputs).</summary>
        public bool IsOutput { get; }

        /// <summary>The zones' TV outputs on this PC's monitors (the page only).</summary>
        public TvOutputs Outputs { get; }

        private void OnScanned(object sender, EventArgs e) => Refresh();

        /// <summary>Stops following the station (a closed TV window).</summary>
        public void Detach() => Ctx.Scanned -= OnScanned;

        private void Refresh()
        {
            string ph = Station.ZonePh(_zone);
            var r = PumpCalc.Live(Ctx, _zone);
            double Ai(string sensor) => Ctx.GetDouble(Station.AiPh("Zone" + _zone + "_" + sensor) + "Pv");

            var source = Station.Source(Ctx.GetInt(ph + "PowerSource.Value"));
            SourceText = source != null ? source.Name : "Güç kaynağı seçilmedi";
            RecordNo = Ctx.GetInt(ph + "SampleCount").ToString(Inv);
            // The running test's number, or the one the next test will get
            TestNo = (Ctx.GetBool(ph + "TestActive") ? Ctx.GetInt(Station.Samples(_zone) + "TestNo") : Ctx.GetInt(Station.NextTestNo)).ToString(Inv);
            Clock = Ctx.GetString("TARIH_SAAT");

            int fmDevice = Ctx.GetInt(ph + "FlowmeterDevice");
            string flowUnit = fmDevice > 0 ? Ctx.GetString(Station.LibraryEntry(Station.Flowmeter, fmDevice) + "Unit") : "";
            _flow.Unit = string.IsNullOrEmpty(flowUnit) ? "m³/h" : flowUnit;

            _flow.Value = Number(r.Flow, "0.0");
            _head.Value = Number(r.Head, "0.00");
            _power.Value = Number(r.PumpPower, "0.00");
            _efficiency.Value = Number(r.Efficiency, "0.0");

            _discharge.Value = Number(Ctx.GetDouble(ph + "DischargePressure"), "0.00");
            _suction.Value = Number(Ctx.GetDouble(ph + "SuctionPressure"), "0.000");
            _dischargeHead.Value = Number(r.DischargeHead, "0.00");
            _suctionHead.Value = Number(r.SuctionHead, "0.00");
            _electrical.Value = Number(r.ElectricalPower, "0.00");
            _voltage.Value = r.Source > 0 ? Number(r.Voltage, "0") : "—";
            _current.Value = r.Source > 0 ? Number(r.Current, "0.0") : "—";
            _speed.Value = Number(Ai("Speed"), "0");
            _torque.Value = Number(Ai("Torque"), "0.0");
            _water.Value = Number(Ai("WaterTemperature"), "0.0");
            _motor.Value = Number(Ai("Temperature"), "0.0");
            _ambient.Value = Number(Ai("AmbientTemperature"), "0.0");
            _vibration.Value = Number(Ai("Vibration"), "0.00");
            _sound.Value = Number(Ai("Sound"), "0.0");

            bool running = Ctx.GetBool(ph + "PumpRunning"), test = Ctx.GetBool(ph + "TestActive");
            if (Ctx.GetBool(ph + "EStop")) { StatusText = "ACİL STOP"; StatusKind = "alarm"; }
            else if (Ctx.GetBool(ph + "SafetyStop")) { StatusText = "MÜDAHALE · TÜM MOTORLAR DURDURULDU"; StatusKind = "alarm"; }
            else if (Ctx.GetBool(ph + "DeviceFault") || Ctx.GetBool(ph + "DriveFault") || Ctx.GetBool(ph + "DriveFeedbackFault")) { StatusText = "ARIZA"; StatusKind = "alarm"; }
            else if (test) { StatusText = running ? "TEST SÜRÜYOR · POMPA ÇALIŞIYOR" : "TEST SÜRÜYOR · POMPA DURUYOR"; StatusKind = "test"; }
            else if (running) { StatusText = "POMPA ÇALIŞIYOR"; StatusKind = "run"; }
            else { StatusText = "TEST BEKLENİYOR"; StatusKind = "idle"; }
        }

        /// <summary>The value in the given format; a value the station cannot measure yet shows a dash.</summary>
        private static string Number(double? v, string format) => v.HasValue ? v.Value.ToString(format, Inv) : "—";
    }
}
