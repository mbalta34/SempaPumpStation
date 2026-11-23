using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Plc.Abstractions;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// An instrument position: which library device is mounted, and the channel it feeds. The mounted device's library
    /// entry applies to the channel at once (the PLC copies it every cycle); the channel's live values, coefficient, offset and moving
    /// average are shown through <see cref="Channel"/> (they are set in the library).
    /// </summary>
    public sealed class PositionRow : ViewModelBase
    {
        public InstrumentPositionInfo Info { get; set; }
        public string Name => Info.Name;
        public string CategoryName => Station.CategoryName(Info.Category);
        public string ZonesText => "Bölge " + string.Join(", ", Info.Zones);
        public IPlcTag DeviceTag { get; set; }     // StationHp.Instruments.<pos>.Device.Value (written)
        public IPlcTag PhDeviceTag { get; set; }   // StationPh.Instruments.<pos>.Device.Value (shown)
        /// <summary>The position's analog input: signal type, the live raw / filtered values, and the coefficient, offset and average loaded from the library.</summary>
        public AnalogSettingRow Channel { get; set; }

        private string _device, _range, _pv;
        private bool _enabled, _rejected;
        public string Device { get => _device; set => Set(ref _device, value); }
        public string Range { get => _range; set => Set(ref _range, value); }
        public string Pv { get => _pv; set => Set(ref _pv, value); }
        /// <summary>The device can be changed (always, also while the zone runs).</summary>
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public bool Rejected { get => _rejected; set => Set(ref _rejected, value); }
    }

    /// <summary>
    /// One library device, editable in a grid: its StationInstruments entry and its StationInstrumentAdjustments entry
    /// (coefficient, offset, moving average). Writes go straight to the DB members.
    /// </summary>
    public sealed class LibraryRow : ViewModelBase
    {
        private readonly HmiContext _ctx;
        private readonly string _e, _a;

        public LibraryRow(HmiContext ctx, int category, int device)
        {
            _ctx = ctx;
            Device = device;
            _e = Station.LibraryEntry(category, device);
            _a = Station.AdjustmentEntry(category, device);
        }

        public int Device { get; }

        public string Description { get => S("Description"); set => W("Description", value ?? ""); }
        public string SerialNumber { get => S("SerialNumber"); set => W("SerialNumber", value ?? ""); }
        public string Unit { get => S("Unit"); set => W("Unit", value ?? ""); }
        public int MinRaw { get => _ctx.GetInt(_e + "MinRaw"); set => W("MinRaw", (short)value); }
        public int MaxRaw { get => _ctx.GetInt(_e + "MaxRaw"); set => W("MaxRaw", (short)value); }
        public double MinPv { get => _ctx.GetReal(_e + "MinPv"); set => W("MinPv", (float)value); }
        public double MaxPv { get => _ctx.GetReal(_e + "MaxPv"); set => W("MaxPv", (float)value); }
        public int FilterType { get => _ctx.GetInt(_e + "FilterType"); set => W("FilterType", (short)value); }
        public double FilterEmaAlpha { get => _ctx.GetReal(_e + "FilterEmaAlpha"); set => W("FilterEmaAlpha", (float)value); }
        public double FilterOutlierMaxJump { get => _ctx.GetReal(_e + "FilterOutlierMaxJump"); set => W("FilterOutlierMaxJump", (float)value); }
        public int FilterOutlierMaxRejectCount { get => _ctx.GetInt(_e + "FilterOutlierMaxRejectCount"); set => W("FilterOutlierMaxRejectCount", (short)value); }
        /// <summary>Pv = scaled value × Coefficient + Offset (a coefficient of 0 is taken as 1).</summary>
        public double Coefficient { get => _ctx.GetReal(_a + "Coefficient"); set => WA("Coefficient", (float)value); }
        public double Offset { get => _ctx.GetReal(_a + "Offset"); set => WA("Offset", (float)value); }
        /// <summary>Moving average over the last n filtered values (one per 100 ms); 0 or 1 = off, at most 100.</summary>
        public int FilterAverageCount
        {
            get => _ctx.GetInt(_a + "FilterAverageCount");
            set => WA("FilterAverageCount", (short)System.Math.Max(0, System.Math.Min(AnalogFilter.AverageMax, value)));
        }

        private string _mountedAt = "";
        public string MountedAt { get => _mountedAt; set => Set(ref _mountedAt, value); }

        private string S(string f) => _ctx.GetString(_e + f);

        private void W(string f, object v)
        {
            _ctx.SetValue(_e + f, v);
            OnPropertyChanged(f);
        }

        private void WA(string f, object v)
        {
            _ctx.SetValue(_a + f, v);
            OnPropertyChanged(f);
        }
    }

    public sealed class InstrumentsViewModel : ScreenBase
    {
        public override string Key => "Instruments";
        public override string Title => "Enstrümanlar";

        public ObservableCollection<PositionRow> Positions { get; } = new ObservableCollection<PositionRow>();
        public ObservableCollection<LibraryRow> Library { get; } = new ObservableCollection<LibraryRow>();
        public ObservableCollection<ChoiceItem> Categories { get; } = new ObservableCollection<ChoiceItem>();

        // Two tabs: the positions (which device is mounted where) and the library (the devices' calibration data)
        public const int PositionsTab = 0, LibraryTab = 1;
        public ObservableCollection<ChoiceItem> Tabs { get; } = new ObservableCollection<ChoiceItem>();
        private int _tab;
        public bool ShowPositions => _tab == PositionsTab;
        public bool ShowLibrary => _tab == LibraryTab;

        public void ShowTab(int tab)
        {
            _tab = tab;
            foreach (var t in Tabs) t.State = t.Value == tab ? "active" : "";
            OnPropertyChanged(nameof(ShowPositions));
            OnPropertyChanged(nameof(ShowLibrary));
        }

        private int _category = Station.Flowmeter;
        public string LibraryTitle => Station.CategoryName(_category).ToUpper(new CultureInfo("tr-TR")) + " KÜTÜPHANESİ (1–" + Station.LibrarySize + ")";

        public InstrumentsViewModel(HmiContext ctx) : base(ctx)
        {
            foreach (var p in Station.Positions)
            {
                Positions.Add(new PositionRow
                {
                    Info = p,
                    DeviceTag = ctx.Tag(Station.PositionHp(p.Name) + "Device.Value"),
                    PhDeviceTag = ctx.Tag(Station.PositionPh(p.Name) + "Device.Value"),
                    Channel = new AnalogSettingRow(ctx, AnalogInputList.All.First(a => a.Name == p.Channel)),
                });
            }

            for (int c = Station.Flowmeter; c <= Station.Vacuummeter; c++)
            {
                int category = c;
                Categories.Add(new ChoiceItem { Value = c, Title = Station.CategoryName(c), Command = new SyncCommand(() => ShowCategory(category)) });
            }
            ShowCategory(Station.Flowmeter);

            Tabs.Add(new ChoiceItem { Value = PositionsTab, Title = "Enstrüman Pozisyonları", Command = new SyncCommand(() => ShowTab(PositionsTab)) });
            Tabs.Add(new ChoiceItem { Value = LibraryTab, Title = "Enstrüman Kütüphanesi", Command = new SyncCommand(() => ShowTab(LibraryTab)) });
            ShowTab(PositionsTab);

            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void ShowCategory(int category)
        {
            _category = category;
            Library.Clear();
            for (int d = 1; d <= Station.LibrarySize; d++) Library.Add(new LibraryRow(Ctx, category, d));
            foreach (var c in Categories) c.State = c.Value == category ? "active" : "";
            OnPropertyChanged(nameof(LibraryTitle));
            RefreshMounted();
        }

        private int _tick;

        private void Refresh()
        {
            // The parameters (and the "mounted at" column) once a second, the live values every scan
            bool parameters = _tick++ % 10 == 0;
            foreach (var r in Positions)
            {
                string ph = Station.PositionPh(r.Name), ai = Station.AiPh(r.Info.Channel);
                int device = Ctx.GetInt(ph + "Device.Value");
                string unit = device > 0 ? Ctx.GetString(Station.LibraryEntry(r.Info.Category, device) + "Unit") : "";
                string desc = device > 0 ? Ctx.GetString(Station.LibraryEntry(r.Info.Category, device) + "Description") : "";

                r.Device = device > 0 ? (string.IsNullOrEmpty(desc) ? "(tanımsız)" : desc) : "Takılı değil";
                r.Range = Ctx.GetDouble(ai + "MinPv.Value").ToString("0.##", CultureInfo.InvariantCulture) + " … "
                        + Ctx.GetDouble(ai + "MaxPv.Value").ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
                r.Channel.RefreshLive();
                if (parameters) r.Channel.RefreshParameters();
                r.Pv = Ctx.GetBool(ai + "WireBreak") ? "Tel kopuk"
                     : Ctx.GetDouble(ai + "Pv").ToString(r.Info.Category == Station.Flowmeter ? "0.0" : "0.000", CultureInfo.InvariantCulture) + " " + unit;
                r.Enabled = Ctx.GetBool(ph + "Device.Enabled");
                r.Rejected = Ctx.GetBool(ph + "SelectionRejected");
            }

            if (parameters) RefreshMounted();
        }

        private void RefreshMounted()
        {
            var mounted = Station.Positions.Where(p => p.Category == _category)
                                           .Select(p => new { p.Name, Device = Ctx.GetInt(Station.PositionPh(p.Name) + "Device.Value") })
                                           .Where(x => x.Device > 0)
                                           .GroupBy(x => x.Device)
                                           .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name)));
            foreach (var row in Library)
            {
                string at;
                row.MountedAt = mounted.TryGetValue(row.Device, out at) ? at : "";
            }
        }
    }
}
