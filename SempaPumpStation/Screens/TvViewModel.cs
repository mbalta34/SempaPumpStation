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
        private string _caption;
        public string Caption { get => _caption; set => Set(ref _caption, value); }
        private string _unit;
        public string Unit { get => _unit; set => Set(ref _unit, value); }
        /// <summary>"run" (green), "accent" (blue) or "" (text colour).</summary>
        public string Kind { get; set; } = "";

        private string _value = "—";
        public string Value { get => _value; set => Set(ref _value, value); }
    }

    /// <summary>
    /// The big display above a test zone (the old "TV EKRANI" page), in the application's theme: the zone, its source and
    /// state, the sample (KAYIT) and test (DENEY) numbers, and Q, Hm, pump power and efficiency as large tiles, readable
    /// from across the hall.
    /// </summary>
    public sealed class TvViewModel : ScreenBase
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public override string Key => "Tv";
        public override string Title => "TV Ekranı";

        public ObservableCollection<ChoiceItem> Zones { get; } = new ObservableCollection<ChoiceItem>();
        /// <summary>Q, Hm and the power and efficiency of the zone's <see cref="EfficiencyBasis"/>: 4 tiles (2 × 2), or 6 (3 × 2) for both bases.</summary>
        public ObservableCollection<TvTile> MainTiles { get; } = new ObservableCollection<TvTile>();

        private readonly TvTile _flow, _head, _power, _efficiency, _shaftPower, _shaftEfficiency;
        private EfficiencyBasis? _basis;

        private int _tileColumns = 2;
        /// <summary>Columns of the tile grid: 2, or 3 with both bases.</summary>
        public int TileColumns { get => _tileColumns; private set => Set(ref _tileColumns, value); }

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

            _flow = new TvTile { Caption = "DEBİ (Q)", Unit = "m³/h", Kind = "run" };
            _head = new TvTile { Caption = "BASMA YÜKSEKLİĞİ (Hm)", Unit = "m", Kind = "accent" };
            _power = new TvTile { Caption = "POMPA GÜCÜ (Pp)", Unit = "kW", Kind = "run" };
            _efficiency = new TvTile { Caption = "VERİM", Unit = "%", Kind = "accent" };
            _shaftPower = new TvTile { Caption = "MİL GÜCÜ (Nmil)", Unit = "kW", Kind = "run" };
            _shaftEfficiency = new TvTile { Caption = "VERİM", Unit = "%", Kind = "accent" };

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
            _shaftPower.Value = Number(r.ShaftPower, "0.00");
            _shaftEfficiency.Value = Number(r.ShaftEfficiency, "0.0");
            Arrange(EfficiencyBasisStore.Get(_zone));

            bool running = Ctx.GetBool(ph + "PumpRunning"), test = Ctx.GetBool(ph + "TestActive");
            if (Ctx.GetBool(ph + "EStop")) { StatusText = "ACİL STOP"; StatusKind = "alarm"; }
            else if (Ctx.GetBool(ph + "SafetyStop")) { StatusText = "MÜDAHALE · TÜM MOTORLAR DURDURULDU"; StatusKind = "alarm"; }
            else if (Ctx.GetBool(ph + "DeviceFault") || Ctx.GetBool(ph + "DriveFault") || Ctx.GetBool(ph + "DriveFeedbackFault")) { StatusText = "ARIZA"; StatusKind = "alarm"; }
            else if (test) { StatusText = running ? "TEST SÜRÜYOR · POMPA ÇALIŞIYOR" : "TEST SÜRÜYOR · POMPA DURUYOR"; StatusKind = "test"; }
            else if (running) { StatusText = "POMPA ÇALIŞIYOR"; StatusKind = "run"; }
            else { StatusText = "TEST BEKLENİYOR"; StatusKind = "idle"; }
        }

        /// <summary>
        /// The tiles of the zone's basis: Q, Hm, Pp and its efficiency (electrical); Q, Hm, Nmil and its efficiency (torque);
        /// both, in rows (Q, Pp, efficiency on Pp / Hm, Nmil, efficiency on Nmil). Rebuilt only when the basis changes.
        /// </summary>
        private void Arrange(EfficiencyBasis basis)
        {
            if (_basis == basis) return;
            _basis = basis;
            bool both = basis == EfficiencyBasis.Both;
            _efficiency.Caption = both ? "VERİM (ELEKTRİK)" : "VERİM";
            _shaftEfficiency.Caption = both ? "VERİM (TORK)" : "VERİM";
            MainTiles.Clear();
            var tiles = basis == EfficiencyBasis.Torque ? new[] { _flow, _head, _shaftPower, _shaftEfficiency }
                      : both ? new[] { _flow, _power, _efficiency, _head, _shaftPower, _shaftEfficiency }
                      : new[] { _flow, _head, _power, _efficiency };
            foreach (var t in tiles) MainTiles.Add(t);
            TileColumns = both ? 3 : 2;
        }

        /// <summary>The value in the given format; a value the station cannot measure yet shows a dash.</summary>
        private static string Number(double? v, string format) => v.HasValue ? v.Value.ToString(format, Inv) : "—";
    }
}
