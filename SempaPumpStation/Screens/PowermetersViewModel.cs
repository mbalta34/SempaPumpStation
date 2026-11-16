using System.Collections.ObjectModel;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>One line of a meter's table: a phase or the totals.</summary>
    public sealed class MeterRow : ViewModelBase
    {
        public string Label { get; set; }
        public string Group { get; set; }

        private string _voltage, _current, _active, _reactive, _apparent, _powerFactor;
        public string Voltage { get => _voltage; set => Set(ref _voltage, value); }
        public string Current { get => _current; set => Set(ref _current, value); }
        public string ActivePower { get => _active; set => Set(ref _active, value); }
        public string ReactivePower { get => _reactive; set => Set(ref _reactive, value); }
        public string ApparentPower { get => _apparent; set => Set(ref _apparent, value); }
        public string PowerFactor { get => _powerFactor; set => Set(ref _powerFactor, value); }
    }

    /// <summary>The PM5550 on one power source.</summary>
    public sealed class MeterCard : ViewModelBase
    {
        public int Source { get; set; }
        public string Name { get; set; }
        public ObservableCollection<MeterRow> Rows { get; } = new ObservableCollection<MeterRow>();

        private bool _commOk;
        private string _usage, _frequency, _energy, _activePower;
        public bool CommOk { get => _commOk; set => Set(ref _commOk, value); }
        /// <summary>Which zone the source feeds.</summary>
        public string Usage { get => _usage; set => Set(ref _usage, value); }
        public string Frequency { get => _frequency; set => Set(ref _frequency, value); }
        public string Energy { get => _energy; set => Set(ref _energy, value); }
        public string ActivePower { get => _activePower; set => Set(ref _activePower, value); }
    }

    /// <summary>
    /// The eight PM5550 energy analysers, one per power source ("Powermeters" DB303, read over Modbus TCP
    /// by the PLC): phase values, totals, frequency, energy and whether the meter answers.
    /// </summary>
    public sealed class PowermetersViewModel : ScreenBase
    {
        public override string Key => "Powermeters";
        public override string Title => "Enerji Analizörleri";

        public ObservableCollection<MeterCard> Meters { get; } = new ObservableCollection<MeterCard>();

        public PowermetersViewModel(HmiContext ctx) : base(ctx)
        {
            foreach (var s in Station.Sources)
            {
                var card = new MeterCard { Source = s.Id, Name = s.Name };
                card.Rows.Add(new MeterRow { Label = "L1", Group = "PhaseA" });
                card.Rows.Add(new MeterRow { Label = "L2", Group = "PhaseB" });
                card.Rows.Add(new MeterRow { Label = "L3", Group = "PhaseC" });
                card.Rows.Add(new MeterRow { Label = "Toplam", Group = "General" });
                Meters.Add(card);
            }

            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void Refresh()
        {
            foreach (var card in Meters)
            {
                string m = Station.Meter(card.Source);
                card.CommOk = Ctx.GetBool(m + "CommOk");
                int zone = Ctx.FedZone(card.Source);
                card.Usage = zone > 0 ? "Bölge " + zone + " besleniyor" : "Bağlı bölge yok";

                foreach (var r in card.Rows)
                {
                    string g = m + r.Group + ".";
                    r.Voltage = Value(card.CommOk, g + "Voltage", "0");
                    r.Current = Value(card.CommOk, g + "Current", "0.0");
                    r.ActivePower = Value(card.CommOk, g + "ActivePower", "0.00");
                    r.ReactivePower = Value(card.CommOk, g + "ReactivePower", "0.00");
                    r.ApparentPower = Value(card.CommOk, g + "ApparentPower", "0.00");
                    r.PowerFactor = Value(card.CommOk, g + "PowerFactor", "0.00");
                }

                card.ActivePower = Value(card.CommOk, m + "General.ActivePower", "0.00");
                card.Frequency = Value(card.CommOk, m + "General.Frequency", "0.00");
                card.Energy = card.CommOk
                    ? (Ctx.GetDouble(m + "General.ActiveEnergy") / Station.MeterEnergyPerKwh).ToString("#,0.0")
                    : "—";
            }
        }

        /// <summary>The value formatted, or a dash while the meter does not answer.</summary>
        private string Value(bool commOk, string tag, string format) => commOk ? Ctx.GetDouble(tag).ToString(format) : "—";
    }
}
