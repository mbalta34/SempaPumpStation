using System.Collections.ObjectModel;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Plc.Abstractions;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// One line of a meter's table: a phase or the totals. The station is a 3-wire system, so a phase line has its
    /// line-to-line voltage (L1: L1-L2, L2: L2-L3, L3: L3-L1) and its current only; the powers and cos φ are totals.
    /// </summary>
    public sealed class MeterRow : ViewModelBase
    {
        public string Label { get; set; }
        public string Group { get; set; }
        public bool IsTotal => Group == "General";

        private string _voltage, _current, _active, _reactive, _apparent, _powerFactor;
        public string Voltage { get => _voltage; set => Set(ref _voltage, value); }
        public string Current { get => _current; set => Set(ref _current, value); }
        public string ActivePower { get => _active; set => Set(ref _active, value); }
        public string ReactivePower { get => _reactive; set => Set(ref _reactive, value); }
        public string ApparentPower { get => _apparent; set => Set(ref _apparent, value); }
        public string PowerFactor { get => _powerFactor; set => Set(ref _powerFactor, value); }
    }

    /// <summary>
    /// The filter settings of an energy meter or the drive (st_Communication_*_PowermeterFilter, FB_Communication_ValueFilter):
    /// none, EMA or EMA + outlier rejection, then an optional moving average, one setting for all values of the device.
    /// </summary>
    public sealed class FilterSettings : ViewModelBase
    {
        private readonly HmiContext _ctx;
        private readonly string _hp, _ph;

        public FilterSettings(HmiContext ctx, string hp, string ph)
        {
            _ctx = ctx;
            _hp = hp;
            _ph = ph;
            AlphaTag = ctx.Tag(ph + "FilterEmaAlpha.Value");
            AlphaWriteTag = ctx.Tag(hp + "FilterEmaAlpha.Value");
            JumpTag = ctx.Tag(ph + "FilterOutlierMaxJump.Value");
            JumpWriteTag = ctx.Tag(hp + "FilterOutlierMaxJump.Value");
            RejectTag = ctx.Tag(ph + "FilterOutlierMaxRejectCount.Value");
            RejectWriteTag = ctx.Tag(hp + "FilterOutlierMaxRejectCount.Value");
            AverageTag = ctx.Tag(ph + "FilterAverageCount.Value");
            AverageWriteTag = ctx.Tag(hp + "FilterAverageCount.Value");
        }

        // Shown: the PLC's value (Ph); written: the HMI's entry (Hp)
        public IPlcTag AlphaTag { get; }
        public IPlcTag AlphaWriteTag { get; }
        /// <summary>The outlier jump, % of the value.</summary>
        public IPlcTag JumpTag { get; }
        public IPlcTag JumpWriteTag { get; }
        public IPlcTag RejectTag { get; }
        public IPlcTag RejectWriteTag { get; }
        public IPlcTag AverageTag { get; }
        public IPlcTag AverageWriteTag { get; }

        /// <summary>0 none, 1 EMA, 2 EMA + outlier rejection (<see cref="AnalogFilter.MeterItems"/>).</summary>
        public int FilterType
        {
            get => _ctx.GetInt(_ph + "FilterType.Value");
            set
            {
                _ctx.WriteParameter(_hp + "FilterType.Value", _ph + "FilterType.Value", (short)value);
                OnPropertyChanged();
            }
        }

        private string _averageText;
        /// <summary>The moving average as a time: "Kapalı" or "20 (2.0 s)".</summary>
        public string AverageText { get => _averageText; set => Set(ref _averageText, value); }

        /// <summary>The combo box is open: the PLC's value is not pushed into it meanwhile.</summary>
        public bool Editing { get; set; }

        public void Refresh()
        {
            AverageText = AnalogFilter.AverageText(_ctx.GetInt(_ph + "FilterAverageCount.Value"));
            if (!Editing) OnPropertyChanged(nameof(FilterType));
        }
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

        /// <summary>The meter's filter (FB_Communication_PowermeterFilter): one setting for all of its values.</summary>
        public FilterSettings Filter { get; set; }
    }

    /// <summary>The ATV660 of the 630 kW VFD ("Drives".Vfd630): motor values read over Modbus TCP by the PLC.</summary>
    public sealed class DriveCard : ViewModelBase
    {
        public string Name => Station.Source(Station.DriveSource).Name + " — ATV660";

        /// <summary>The drive's filter (FB_Communication_DriveFilter): one setting for all of its values.</summary>
        public FilterSettings Filter { get; set; }

        private bool _commOk, _extraCommOk;
        private string _usage, _status, _frequency, _current, _motorVoltage, _mainsVoltage, _torque, _torqueNm, _power, _mechanicalKw, _electricalKw, _powerFactor;
        public bool CommOk { get => _commOk; set => Set(ref _commOk, value); }
        /// <summary>The second request (torque in Nm, powers in kW) is answered.</summary>
        public bool ExtraCommOk { get => _extraCommOk; set => Set(ref _extraCommOk, value); }
        public string Usage { get => _usage; set => Set(ref _usage, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        private bool _extraMissing;
        /// <summary>The drive answers its main request but not the second one (Nm, kW): kW and cos φ are unknown.</summary>
        public bool ExtraMissing { get => _extraMissing; set => Set(ref _extraMissing, value); }
        public string Frequency { get => _frequency; set => Set(ref _frequency, value); }
        public string Current { get => _current; set => Set(ref _current, value); }
        public string MotorVoltage { get => _motorVoltage; set => Set(ref _motorVoltage, value); }
        public string MainsVoltage { get => _mainsVoltage; set => Set(ref _mainsVoltage, value); }
        public string Torque { get => _torque; set => Set(ref _torque, value); }
        public string TorqueNm { get => _torqueNm; set => Set(ref _torqueNm, value); }
        public string Power { get => _power; set => Set(ref _power, value); }
        public string MechanicalKw { get => _mechanicalKw; set => Set(ref _mechanicalKw, value); }
        public string ElectricalKw { get => _electricalKw; set => Set(ref _electricalKw, value); }
        /// <summary>The motor side's cos φ, output kW / (√3·U·I), as a zone on the drive measures with (FC_Station_DriveElectrical).</summary>
        public string PowerFactor { get => _powerFactor; set => Set(ref _powerFactor, value); }

        /// <summary>The DRIVECOM state of the status word (ETA): fault, running (operation enabled) or stopped.</summary>
        public static string StatusText(int eta) =>
            (eta & 0x0008) != 0 ? "Arıza" : (eta & 0x006F) == 0x0027 ? "Çalışıyor" : "Duruyor";
    }

    /// <summary>
    /// The eight PM5550 energy analysers, one per power source ("Powermeters" DB303, read over Modbus TCP
    /// by the PLC and filtered per meter): phase values, totals, frequency, energy, whether the meter answers, and
    /// the meter's filter setting.
    /// </summary>
    public sealed class PowermetersViewModel : ScreenBase
    {
        public override string Key => "Powermeters";
        public override string Title => "Enerji Analizörleri";

        public ObservableCollection<MeterCard> Meters { get; } = new ObservableCollection<MeterCard>();
        public DriveCard Drive { get; } = new DriveCard();

        public PowermetersViewModel(HmiContext ctx) : base(ctx)
        {
            foreach (var s in Station.Sources)
            {
                var card = new MeterCard { Source = s.Id, Name = s.Name };
                card.Filter = new FilterSettings(ctx, Station.MeterFilterHp(s.Id), Station.MeterFilterPh(s.Id));
                card.Rows.Add(new MeterRow { Label = "L1", Group = "PhaseA" });
                card.Rows.Add(new MeterRow { Label = "L2", Group = "PhaseB" });
                card.Rows.Add(new MeterRow { Label = "L3", Group = "PhaseC" });
                card.Rows.Add(new MeterRow { Label = "Toplam", Group = "General" });
                Meters.Add(card);
            }

            Drive.Filter = new FilterSettings(ctx, Station.DriveFilterHp, Station.DriveFilterPh);
            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void Refresh()
        {
            RefreshDrive();
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
                    // 3-wire system: the powers and cos φ are totals only
                    r.ActivePower = r.IsTotal ? Value(card.CommOk, g + "ActivePower", "0.00") : "";
                    r.ReactivePower = r.IsTotal ? Value(card.CommOk, g + "ReactivePower", "0.00") : "";
                    r.ApparentPower = r.IsTotal ? Value(card.CommOk, g + "ApparentPower", "0.00") : "";
                    r.PowerFactor = r.IsTotal ? Value(card.CommOk, g + "PowerFactor", "0.00") : "";
                }
                card.Filter.Refresh();

                card.ActivePower = Value(card.CommOk, m + "General.ActivePower", "0.00");
                card.Frequency = Value(card.CommOk, m + "General.Frequency", "0.00");
                card.Energy = card.CommOk
                    ? (Ctx.GetDouble(m + "General.ActiveEnergy") / Station.MeterEnergyPerKwh).ToString("#,0.0")
                    : "—";
            }
        }

        private void RefreshDrive()
        {
            string d = Station.Drive630;
            var card = Drive;
            card.CommOk = Ctx.GetBool(d + "CommOk");
            card.ExtraCommOk = Ctx.GetBool(d + "ExtraCommOk");
            card.ExtraMissing = card.CommOk && !card.ExtraCommOk;
            int zone = Ctx.FedZone(Station.DriveSource);
            card.Usage = zone > 0 ? "Bölge " + zone + " besleniyor" : "Bağlı bölge yok";
            card.Status = card.CommOk ? DriveCard.StatusText(Ctx.GetInt(d + "StatusWord")) : "—";
            card.Frequency = Value(card.CommOk, d + "OutputFrequency", "0.0");
            card.Current = Value(card.CommOk, d + "MotorCurrent", "0.0");
            card.MotorVoltage = Value(card.CommOk, d + "MotorVoltage", "0");
            card.MainsVoltage = Value(card.CommOk, d + "MainsVoltage", "0");
            card.Torque = Value(card.CommOk, d + "MotorTorque", "0.0");
            card.Power = Value(card.CommOk, d + "MotorPower", "0");
            card.TorqueNm = Value(card.ExtraCommOk, d + "MotorTorqueNm", "0");
            card.MechanicalKw = Value(card.ExtraCommOk, d + "MotorMechanicalPower", "0.0");
            card.ElectricalKw = Value(card.ExtraCommOk, d + "OutputElectricalPower", "0.0");
            var motor = PumpCalc.DriveMotorSide(card.CommOk, card.ExtraCommOk, Ctx.GetDouble(d + "MotorVoltage"),
                                                Ctx.GetDouble(d + "MotorCurrent"), Ctx.GetDouble(d + "OutputElectricalPower"));
            card.PowerFactor = motor != null ? System.Math.Abs(motor.PowerFactor).ToString("0.00") : "—";
            card.Filter.Refresh();
        }

        /// <summary>The value formatted, or a dash while the meter does not answer.</summary>
        private string Value(bool commOk, string tag, string format) => commOk ? Ctx.GetDouble(tag).ToString(format) : "—";
    }
}
