using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// One fixed analog sensor (torque, speed, sound, temperatures, vibration): its scaling and filter parameters in
    /// "MaintenanceHp".Io.AnalogInputs, written like any HMI parameter, and its live values. Instrument positions are not
    /// listed: their parameters come from the instrument library.
    /// </summary>
    public sealed class AnalogSettingRow : ViewModelBase
    {
        private readonly HmiContext _ctx;
        private readonly string _hp, _ph;

        public AnalogSettingRow(HmiContext ctx, AnalogInputInfo info)
        {
            _ctx = ctx;
            Info = info;
            _hp = Station.AiHp(info.Name);
            _ph = Station.AiPh(info.Name);
        }

        public AnalogInputInfo Info { get; }
        public string Zone => Info.Group;
        public string Label => Info.Label;
        public string Unit => Info.Unit;
        public string Channel => Info.Name;

        // The PLC's values (Ph); an entry goes to Hp and the PLC takes it over
        public int MinRaw { get => I("MinRaw"); set => W("MinRaw", (short)value); }
        public int MaxRaw { get => I("MaxRaw"); set => W("MaxRaw", (short)value); }
        public double MinPv { get => R("MinPv"); set => W("MinPv", (float)value); }
        public double MaxPv { get => R("MaxPv"); set => W("MaxPv", (float)value); }
        public int FilterType { get => I("FilterType"); set => W("FilterType", (short)value); }
        public double FilterEmaAlpha { get => R("FilterEmaAlpha"); set => W("FilterEmaAlpha", (float)value); }
        public double FilterOutlierMaxJump { get => R("FilterOutlierMaxJump"); set => W("FilterOutlierMaxJump", (float)value); }
        public int FilterOutlierMaxRejectCount { get => I("FilterOutlierMaxRejectCount"); set => W("FilterOutlierMaxRejectCount", (short)value); }

        private string _raw = "", _filtered = "", _value = "";
        public string Raw { get => _raw; private set => Set(ref _raw, value); }
        public string Filtered { get => _filtered; private set => Set(ref _filtered, value); }
        public string Value { get => _value; private set => Set(ref _value, value); }

        /// <summary>A cell of this row is being edited: its parameters are not refreshed meanwhile.</summary>
        public bool Editing { get; set; }

        private int I(string n) => _ctx.GetInt(_ph + n + ".Value");
        private double R(string n) => _ctx.GetReal(_ph + n + ".Value");

        private void W(string n, object v)
        {
            _ctx.WriteParameter(_hp + n + ".Value", _ph + n + ".Value", v);
            OnPropertyChanged(n);
        }

        public void RefreshLive()
        {
            bool wireBreak = _ctx.GetBool(_ph + "WireBreak");
            Raw = _ctx.GetInt(_ph + "RawValue").ToString(CultureInfo.InvariantCulture);
            Filtered = _ctx.GetDouble(_ph + "FilteredRaw").ToString("0", CultureInfo.InvariantCulture);
            Value = wireBreak ? "Tel kopuk" : _ctx.GetDouble(_ph + "Pv").ToString("0.0#", CultureInfo.InvariantCulture) + " " + Unit;
        }

        /// <summary>Shows the PLC's parameter values again (they can also change from TIA or another HMI).</summary>
        public void RefreshParameters()
        {
            if (Editing) return;
            foreach (var n in Station.AiRealParameters.Concat(Station.AiIntParameters)) OnPropertyChanged(n);
        }
    }

    public sealed class AnalogSettingsViewModel : ScreenBase
    {
        public override string Key => "AnalogSettings";
        public override string Title => "Analog Ayarları";

        public ObservableCollection<AnalogSettingRow> Rows { get; } = new ObservableCollection<AnalogSettingRow>();

        private int _tick;

        public AnalogSettingsViewModel(HmiContext ctx) : base(ctx)
        {
            foreach (var a in AnalogInputList.All.Where(a => a.Position == null)) Rows.Add(new AnalogSettingRow(ctx, a));
            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void Refresh()
        {
            bool parameters = _tick++ % 10 == 0;   // the parameters once a second, the live values every scan
            foreach (var r in Rows)
            {
                r.RefreshLive();
                if (parameters) r.RefreshParameters();
            }
        }
    }
}
