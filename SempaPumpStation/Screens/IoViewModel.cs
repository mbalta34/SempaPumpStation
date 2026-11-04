using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>One digital input in the watch table.</summary>
    public sealed class InputRow : ViewModelBase
    {
        public DigitalInputInfo Info { get; set; }
        public string Label => Info.Label;
        public string Name => Info.Name;

        private bool _pv, _forcing, _wireBreak;
        public bool Pv { get => _pv; set => Set(ref _pv, value); }
        public bool Forcing { get => _forcing; set => Set(ref _forcing, value); }
        public bool WireBreak { get => _wireBreak; set => Set(ref _wireBreak, value); }
    }

    public sealed class InputGroup
    {
        public string Title { get; set; }
        public ObservableCollection<InputRow> Rows { get; } = new ObservableCollection<InputRow>();
    }

    /// <summary>One analog input in the watch table.</summary>
    public sealed class AnalogRow : ViewModelBase
    {
        public AnalogInputInfo Info { get; set; }
        public string Label => Info.Label;
        public string Name => Info.Name;

        private string _value = "", _raw = "", _filtered = "", _range = "", _status = "";
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Raw { get => _raw; set => Set(ref _raw, value); }
        public string Filtered { get => _filtered; set => Set(ref _filtered, value); }
        public string Range { get => _range; set => Set(ref _range, value); }
        /// <summary>"Tel kopuk", "Zorlanıyor" or empty.</summary>
        public string Status { get => _status; set => Set(ref _status, value); }
    }

    /// <summary>One analog output (VFD speed reference) in the watch table.</summary>
    public sealed class AnalogOutputRow : ViewModelBase
    {
        public AnalogOutputInfo Info { get; set; }
        public string Label => Info.Label;
        public string Name => Info.Name;

        private string _value = "", _raw = "", _range = "", _status = "";
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Raw { get => _raw; set => Set(ref _raw, value); }
        public string Range { get => _range; set => Set(ref _range, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
    }

    public sealed class AnalogGroup
    {
        public string Title { get; set; }
        public ObservableCollection<AnalogRow> Rows { get; } = new ObservableCollection<AnalogRow>();
    }

    /// <summary>
    /// IO watch and force (as in BluePrism's maintenance HMI): the digital and analog inputs and the analog outputs of
    /// "Io" live, and the digital output force table.
    /// </summary>
    public sealed class IoViewModel : ScreenBase
    {
        public const int DigitalInputsTab = 0, AnalogInputsTab = 1, DigitalOutputsTab = 2, AnalogOutputsTab = 3;

        public override string Key => "Io";
        public override string Title => "IO İzleme ve Zorlama";

        public ObservableCollection<ChoiceItem> Tabs { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<InputGroup> InputGroups { get; } = new ObservableCollection<InputGroup>();
        public ObservableCollection<AnalogGroup> AnalogGroups { get; } = new ObservableCollection<AnalogGroup>();
        public ObservableCollection<AnalogOutputRow> AnalogOutputs { get; } = new ObservableCollection<AnalogOutputRow>();
        public OutputForceViewModel Outputs { get; }

        private int _tab = -1;
        public int Tab => _tab;
        public bool ShowDigitalInputs => _tab == DigitalInputsTab;
        public bool ShowAnalogInputs => _tab == AnalogInputsTab;
        public bool ShowDigitalOutputs => _tab == DigitalOutputsTab;
        public bool ShowAnalogOutputs => _tab == AnalogOutputsTab;

        public IoViewModel(HmiContext ctx) : base(ctx)
        {
            Outputs = new OutputForceViewModel(ctx);

            string[] titles = { "Dijital Girişler (" + DigitalInputs.All.Length + ")", "Analog Girişler (" + AnalogInputList.All.Length + ")",
                                "Dijital Çıkışlar — Zorlama (" + DigitalOutputs.All.Length + ")", "Analog Çıkışlar (" + AnalogOutputList.All.Length + ")" };
            for (int i = 0; i < titles.Length; i++)
            {
                int tab = i;
                Tabs.Add(new ChoiceItem { Value = i, Title = titles[i], Command = new RelayCommand(() => ShowTab(tab)) });
            }

            for (int z = 1; z <= Station.ZoneCount; z++) InputGroups.Add(Inputs("BÖLGE " + z, z));
            InputGroups.Add(Inputs("SÜRÜCÜLER (MERKEZİ PANO)", 0));

            foreach (var title in AnalogInputList.Groups)
            {
                var group = new AnalogGroup { Title = title };
                foreach (var a in AnalogInputList.All.Where(a => a.Group == title)) group.Rows.Add(new AnalogRow { Info = a });
                AnalogGroups.Add(group);
            }
            foreach (var o in AnalogOutputList.All) AnalogOutputs.Add(new AnalogOutputRow { Info = o });

            ShowTab(DigitalInputsTab);
            ctx.Scanned += (o, e) => Refresh();
        }

        private static InputGroup Inputs(string title, int zone)
        {
            var group = new InputGroup { Title = title };
            foreach (var i in DigitalInputs.OfZone(zone)) group.Rows.Add(new InputRow { Info = i });
            return group;
        }

        public void ShowTab(int tab)
        {
            _tab = tab;
            foreach (var t in Tabs) t.State = t.Value == tab ? "active" : "";
            OnPropertyChanged(nameof(Tab));
            OnPropertyChanged(nameof(ShowDigitalInputs));
            OnPropertyChanged(nameof(ShowAnalogInputs));
            OnPropertyChanged(nameof(ShowDigitalOutputs));
            OnPropertyChanged(nameof(ShowAnalogOutputs));
            Refresh();
        }

        // Only the tab on screen is refreshed (the output table keeps itself current)
        private void Refresh()
        {
            if (ShowDigitalInputs)
                foreach (var row in InputGroups.SelectMany(g => g.Rows))
                {
                    row.Pv = Ctx.GetBool(row.Info.Ph + "Pv");
                    row.Forcing = Ctx.GetBool(row.Info.Ph + "ForcingActive");
                    row.WireBreak = Ctx.GetBool(row.Info.Ph + "WireBreak");
                }
            else if (ShowAnalogInputs)
                foreach (var row in AnalogGroups.SelectMany(g => g.Rows)) RefreshAnalog(row);
            else if (ShowAnalogOutputs)
                foreach (var row in AnalogOutputs)
                {
                    string ph = row.Info.Ph;
                    row.Value = Number(ph + "Pv", "0.0") + " %";
                    row.Raw = Ctx.GetInt(ph + "RawValue").ToString(CultureInfo.InvariantCulture);
                    row.Range = Number(ph + "MinPv.Value", "0.##") + " … " + Number(ph + "MaxPv.Value", "0.##") + " %";
                    row.Status = Ctx.GetBool(ph + "ForcingActive") ? "Zorlanıyor" : "";
                }
        }

        private void RefreshAnalog(AnalogRow row)
        {
            string ph = row.Info.Ph, unit = row.Info.Unit;
            var position = row.Info.Position;
            if (position != null)
            {
                int device = Ctx.GetInt(Station.PositionPh(position.Name) + "Device.Value");
                unit = device > 0 ? Ctx.GetString(Station.LibraryEntry(position.Category, device) + "Unit") : "";
            }
            string format = position != null && position.Category != Station.Flowmeter ? "0.000" : "0.0";

            bool wireBreak = Ctx.GetBool(ph + "WireBreak");
            row.Value = wireBreak ? "—" : (Number(ph + "Pv", format) + " " + unit).Trim();
            row.Raw = Ctx.GetInt(ph + "RawValue").ToString(CultureInfo.InvariantCulture);
            row.Filtered = Number(ph + "FilteredRaw", "0");
            row.Range = (Number(ph + "MinPv.Value", "0.##") + " … " + Number(ph + "MaxPv.Value", "0.##") + " " + unit).Trim();
            row.Status = wireBreak ? "Tel kopuk" : Ctx.GetBool(ph + "ForcingActive") ? "Zorlanıyor" : "";
        }

        private string Number(string tag, string format) => Ctx.GetDouble(tag).ToString(format, CultureInfo.InvariantCulture);
    }
}
