using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// One analog input channel: its scaling, filter, moving average, coefficient and offset parameters in
    /// "MaintenanceHp".Io.AnalogInputs, written like any HMI parameter, and its live values (raw and filtered, as counts
    /// and as mA / V by the signal type the PLC program gives the channel). Analog Ayarları lists the fixed sensors
    /// (torque, speed, sound, temperatures, vibration); an instrument position uses it for its channel's live values,
    /// coefficient, offset and moving average (its scaling and filter come from the instrument library).
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
        /// <summary>Pv = scaled value × Coefficient + Offset.</summary>
        public double Coefficient { get => R("Coefficient"); set => W("Coefficient", (float)value); }
        public double Offset { get => R("Offset"); set => W("Offset", (float)value); }
        /// <summary>The channel's moving average over the last n filtered values (one per 100 ms); 0 or 1 = off, max 100.</summary>
        public int FilterAverageCount { get => I("FilterAverageCount"); set => W("FilterAverageCount", (short)value); }

        private string _signal = "", _raw = "", _filtered = "", _rawSignal = "", _filteredSignal = "", _value = "";
        /// <summary>The signal type the PLC program passes to the channel (not set on the HMI).</summary>
        public string Signal { get => _signal; private set => Set(ref _signal, value); }
        /// <summary>The raw value as the analog card / hub reports it, and after the filter (counts).</summary>
        public string Raw { get => _raw; private set => Set(ref _raw, value); }
        public string Filtered { get => _filtered; private set => Set(ref _filtered, value); }
        /// <summary>The same two as the signal, mA or V.</summary>
        public string RawSignal { get => _rawSignal; private set => Set(ref _rawSignal, value); }
        public string FilteredSignal { get => _filteredSignal; private set => Set(ref _filteredSignal, value); }
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

        public void RefreshLive(string valueFormat = "0.0#")
        {
            bool wireBreak = _ctx.GetBool(_ph + "WireBreak");
            int type = _ctx.GetInt(_ph + "SignalType");
            Signal = AnalogSignal.Name(type);
            Raw = _ctx.GetInt(_ph + "RawValue").ToString(CultureInfo.InvariantCulture);
            Filtered = _ctx.GetDouble(_ph + "FilteredRaw").ToString("0", CultureInfo.InvariantCulture);
            RawSignal = AnalogSignal.Format(type, _ctx.GetDouble(_ph + "RawSignal"));
            FilteredSignal = AnalogSignal.Format(type, _ctx.GetDouble(_ph + "FilteredSignal"));
            Value = wireBreak ? "Tel kopuk" : _ctx.GetDouble(_ph + "Pv").ToString(valueFormat, CultureInfo.InvariantCulture) + " " + Unit;
        }

        /// <summary>Shows the PLC's parameter values again (they can also change from TIA or another HMI).</summary>
        public void RefreshParameters()
        {
            if (Editing) return;
            foreach (var n in Station.AiRealParameters.Concat(Station.AiIntParameters).Concat(Station.AiChannelRealParameters).Concat(Station.AiChannelIntParameters))
                OnPropertyChanged(n);
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
