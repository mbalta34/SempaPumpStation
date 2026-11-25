using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>The live pump figures of one zone, as the zone and TV screens show them.</summary>
    public sealed class PumpReading
    {
        public double Flow { get; set; }             // m³/h
        /// <summary>Total head Hm, m, velocity head included (report column I).</summary>
        public double Head { get; set; }
        /// <summary>Hb: discharge head, m (the report's Hb in bar × 10.2).</summary>
        public double DischargeHead { get; set; }
        /// <summary>He: suction head, m, as measured, negative under vacuum (the report's F in bar × 10.2); 0 for a submersible pump.</summary>
        public double SuctionHead { get; set; }
        /// <summary>Velocity head difference between the discharge and suction pipes, m (report column H).</summary>
        public double VelocityHead { get; set; }
        public double HydraulicPower { get; set; }   // kW
        /// <summary>Nş: electrical input √3·U·I·cosφ, kW, from the selected source's PM5550 (report column M); null when unknown.</summary>
        public double? ElectricalPower { get; set; }
        /// <summary>Pp / Np: pump (shaft) power, Nş × motor efficiency at its load (report column N); null when unknown.</summary>
        public double? PumpPower { get; set; }
        /// <summary>Pump efficiency, %: hydraulic power over pump power (report column O); null when the pump power is unknown.</summary>
        public double? Efficiency { get; set; }
        /// <summary>The source whose values gave <see cref="ElectricalPower"/>, 0 = none.</summary>
        public int Source { get; set; }
        /// <summary>U (line to line, V) and I (A) behind <see cref="ElectricalPower"/>.</summary>
        public double Voltage { get; set; }
        public double Current { get; set; }
        /// <summary>cos φ behind <see cref="ElectricalPower"/>, its magnitude (the meter's sign is the quadrant).</summary>
        public double PowerFactor { get; set; }
        /// <summary>The values are the 630 kW VFD's motor side, from its ATV660, not its PM5550.</summary>
        public bool ElectricalFromDrive { get; set; }
        /// <summary>Torque meter (Nm) and speed (rpm) of the zone.</summary>
        public double Torque { get; set; }
        public double Speed { get; set; }
        /// <summary>Nmil: shaft power from the torque meter, T × n / 9549, kW (report column R); null without torque or speed.</summary>
        public double? ShaftPower { get; set; }
        /// <summary>Pump efficiency on the shaft power × the mechanical efficiency, % (report column S); null while the shaft power is unknown.</summary>
        public double? ShaftEfficiency { get; set; }
    }

    /// <summary>U, I and cos φ a zone measures with on a source (FB_Station_Main, FC_Station_DriveElectrical).</summary>
    public sealed class ZoneElectricalValues
    {
        public double Voltage, Current, PowerFactor;
        /// <summary>From the drive's motor side (630 kW VFD) rather than the source's PM5550.</summary>
        public bool FromDrive;
        /// <summary>The device the values come from answers.</summary>
        public bool Valid;
    }

    /// <summary>
    /// The test line and motor data the formulas need, from the recipes (live) or an archived test's header:
    /// pipe diameters, geodetic height, submersible pump, rated power and the motor's efficiency at 1/4..4/4 load.
    /// </summary>
    public sealed class PumpLine
    {
        public double SuctionDn;      // mm, pump suction diameter (report M13)
        public double DischargeDn;    // mm, discharge line inner diameter, else the pump's discharge diameter (report M14)
        public double Geodetic;       // m, height difference between the gauges (report M15)
        public bool Submersible;      // no suction gauge: suction is ignored
        public double RatedKw;        // motor rated power (report D10)
        public double[] MotorEfficiency = new double[5];   // % at 1/4..4/4 load, index 1..4 (report G8, F8, E8, D8)
        /// <summary>Plant efficiency factor (report M10): 1 as in the template.</summary>
        public double PlantEfficiency = 1.0;
        /// <summary>Mechanical efficiency factor (report M11), from the pump recipe's Mekanik Verim (%); 1 without one.</summary>
        public double MechanicalEfficiency = 1.0;

        /// <summary>From recipe / report header values by key (the keys of <see cref="TestArchive.HeaderKeys"/>).</summary>
        public static PumpLine From(Func<string, string> get)
        {
            double suction = PumpCalc.FirstNumber(get("WRT_PMP_EMME_CAP"));
            double line = PumpCalc.FirstNumber(get("HAT_BAS_IC_CAPI")), pumpDischarge = PumpCalc.FirstNumber(get("WRT_PMP_BAS_CAP"));
            var l = new PumpLine
            {
                SuctionDn = suction,
                DischargeDn = line > 0 ? line : pumpDischarge,
                Geodetic = PumpCalc.FirstNumber(get("GEO_YUKSEKLIK"), signed: true),
                Submersible = string.Equals(get("DALGIC_PMP_SEL"), "True", StringComparison.OrdinalIgnoreCase),
                RatedKw = PumpCalc.FirstNumber(get("WRT_MOTOR_GUCU")),
            };
            for (int q = 1; q <= 4; q++) l.MotorEfficiency[q] = PumpCalc.FirstNumber(get("WRT_MOTOR_VERIM_" + q + "_4"));
            l.MechanicalEfficiency = PumpCalc.EfficiencyFactor(get("WRT_PMP_MEKANIK_VERIM"));
            return l;
        }
    }

    /// <summary>
    /// The pump-test formulas, in one place for the live screens, the archive and the report screen. They are the
    /// formulas of the Excel report template (Reports\Sempa_Rapor_Sablon.xlsx, row 32 onwards), so the live figures,
    /// the archive and the Excel report agree.
    /// </summary>
    public static class PumpCalc
    {
        /// <summary>Metres of water column per bar (report F16).</summary>
        public const double MetresPerBar = 10.2;

        /// <summary>Q [m³/h] × H [m] / 367.2 gives the hydraulic power in kW (the report's Q [l/s] × H / 102).</summary>
        public const double HydraulicPowerDivisor = 367.2;

        private const double G = 9.81, Pi = 3.141, Sqrt3 = 1.732;   // as the template writes them

        /// <summary>
        /// Velocity head difference v²/2g between the discharge and the suction pipe (report column H); 0 without both
        /// diameters, and never negative.
        /// </summary>
        public static double VelocityHead(double flow, PumpLine line)
        {
            if (line.SuctionDn <= 0 || line.DischargeDn <= 0) return 0.0;
            double k = 16.0 / (3.6 * 3.6 * 2 * G);
            double d = flow * 1000.0 / (Pi * line.DischargeDn * line.DischargeDn);
            double s = flow * 1000.0 / (Pi * line.SuctionDn * line.SuctionDn);
            return Math.Max(0.0, k * (d * d - s * s));
        }

        /// <summary>Hb, the discharge head in m (report: Hb [bar] × F16).</summary>
        public static double DischargeHead(double dischargeBar) => dischargeBar * MetresPerBar;

        /// <summary>He, the suction head in m as the transmitter reads it, negative under vacuum (report: F [bar] × F16); 0 for a submersible pump.</summary>
        public static double SuctionHead(double suctionBar, PumpLine line) => line.Submersible ? 0.0 : suctionBar * MetresPerBar;

        /// <summary>
        /// Total head (report column I): Hb − He, plus the velocity head difference and the height between the gauges. A
        /// submersible pump has no suction gauge, so suction is ignored.
        /// </summary>
        public static double Head(double dischargeBar, double suctionBar, double flow, PumpLine line)
            => DischargeHead(dischargeBar) - SuctionHead(suctionBar, line) + VelocityHead(flow, line) + line.Geodetic;

        public static double HydraulicPower(double flow, double head) => flow * head / HydraulicPowerDivisor;

        /// <summary>
        /// Nş, the electrical input √3·U·I·cosφ in kW (report column M), from the line voltage, current and power factor;
        /// not the meter's own active power. The meter's power factor carries the quadrant as its sign (leading /
        /// lagging), so its magnitude is used. 0 without a valid cos φ.
        /// </summary>
        public static double ElectricalPower(double voltage, double current, double powerFactor)
        {
            double cos = Math.Abs(powerFactor);
            return cos > 0 && cos <= 1 ? Sqrt3 * cos * voltage * current / 1000.0 : 0.0;
        }

        /// <summary>
        /// The electrical values of a zone on <paramref name="source"/>, as FB_Station_Main takes them: the source's PM5550, except
        /// on the 630 kW VFD while its ATV660 answers both requests, whose meter sits on the drive's input. There the motor side
        /// counts (FC_Station_DriveElectrical): U = motor voltage, I = motor current, cos φ = output kW / (1.732 · U · I), so
        /// Nş is the drive's output electrical power. Null while no source is selected.
        /// </summary>
        /// <param name="real">Reads a REAL tag.</param>
        /// <param name="flag">Reads a BOOL tag.</param>
        public static ZoneElectricalValues ZoneElectrical(Func<string, double> real, Func<string, bool> flag, int source)
        {
            if (source < 1 || source > Station.SourceCount) return null;
            string m = Station.Meter(source);
            var e = new ZoneElectricalValues
            {
                Voltage = real(m + "General.Voltage"),
                Current = real(m + "General.Current"),
                PowerFactor = real(m + "General.PowerFactor"),
                Valid = flag(m + "CommOk"),
            };
            if (source == Station.DriveSource)
            {
                string d = Station.Drive630;
                var drive = DriveMotorSide(flag(d + "CommOk"), flag(d + "ExtraCommOk"),
                                           real(d + "MotorVoltage"), real(d + "MotorCurrent"), real(d + "OutputElectricalPower"));
                if (drive != null)
                {
                    e.Voltage = drive.Voltage;
                    e.Current = drive.Current;
                    e.PowerFactor = drive.PowerFactor;
                    e.FromDrive = true;
                    e.Valid = true;
                }
            }
            return e;
        }

        /// <summary>FC_Station_DriveElectrical: the drive's motor side as U, I and cos φ; null unless both requests are answered.</summary>
        public static ZoneElectricalValues DriveMotorSide(bool commOk, bool extraCommOk, double motorVoltage, double motorCurrent, double outputKw)
        {
            if (!(commOk && extraCommOk)) return null;
            double apparentKva = Sqrt3 * motorVoltage * motorCurrent / 1000.0;
            double cos = apparentKva > 0.001 ? Math.Max(-1.0, Math.Min(1.0, outputKw / apparentKva)) : 0.0;
            return new ZoneElectricalValues { Voltage = motorVoltage, Current = motorCurrent, PowerFactor = cos, FromDrive = true, Valid = true };
        }

        /// <summary>
        /// Np, the pump power (report column N): Nş times the motor's efficiency at its load band (up to 25, 50, 75 % of
        /// the rated power, above that full load) and the plant and mechanical factors. Null without rated power or efficiency.
        /// </summary>
        public static double? PumpPower(double electricalPower, PumpLine line)
        {
            if (line.RatedKw <= 0 || electricalPower <= 0) return null;
            double load = electricalPower / line.RatedKw * 100.0;
            int band = load <= 25 ? 1 : load <= 50 ? 2 : load <= 75 ? 3 : 4;
            double eff = line.MotorEfficiency[band];
            if (eff <= 0) return null;
            return electricalPower * eff / 100.0 * line.PlantEfficiency * line.MechanicalEfficiency;
        }

        /// <summary>
        /// Nmil, the shaft power from the torque meter in kW (report column R): P [W] = T [Nm] × n [rpm] / 9.549, so
        /// kW = T × n / 9549. Null without torque or speed.
        /// </summary>
        public static double? ShaftPower(double torque, double speed)
            => torque > 0 && speed > 0 ? torque * speed / 9549.0 : (double?)null;

        /// <summary>
        /// The power the pump gets from the torque meter's shaft power: Nmil × the mechanical efficiency (report R × AB), the
        /// losses between the meter and the pump taken off as Np takes them off the motor's output.
        /// </summary>
        public static double? PumpShaftPower(double? shaftPower, PumpLine line)
            => shaftPower.HasValue ? shaftPower.Value * line.MechanicalEfficiency : (double?)null;

        /// <summary>Pump efficiency in % (report column O): hydraulic power over pump power; null when that is unknown or zero.
        /// With the shaft power (report column S) it is the efficiency on the torque meter.</summary>
        public static double? Efficiency(double hydraulicPower, double? pumpPower)
            => pumpPower.HasValue && pumpPower.Value > 0 ? hydraulicPower / pumpPower.Value * 100.0 : (double?)null;

        /// <summary>The line and motor data of the recipes now loaded.</summary>
        public static PumpLine LiveLine(HmiContext ctx)
            => PumpLine.From(k => Convert.ToString(ctx.Tag(k)?.TagValue, CultureInfo.InvariantCulture) ?? "");

        /// <summary>The live figures of a zone from its StationPh values, its source's meter and the recipes.</summary>
        public static PumpReading Live(HmiContext ctx, int zone)
        {
            string ph = Station.ZonePh(zone);
            var line = LiveLine(ctx);
            double flow = ctx.GetDouble(ph + "Flow");
            double velocity = VelocityHead(flow, line);
            double pd = ctx.GetDouble(ph + "DischargePressure"), ps = ctx.GetDouble(ph + "SuctionPressure");
            double head = Head(pd, ps, flow, line);
            double hydraulic = HydraulicPower(flow, head);

            // Nş from the zone's selected source, as each sample stores it (U, I, cos φ): its PM5550, or on the 630 kW VFD the
            // drive's motor side; unknown while no source is selected or its device does not answer
            int source = ctx.GetInt(ph + "PowerSource.Value");
            var e = ZoneElectrical(ctx.GetDouble, ctx.GetBool, source);
            double? electrical = e != null && e.Valid ? ElectricalPower(e.Voltage, e.Current, e.PowerFactor) : (double?)null;
            double? pump = electrical.HasValue ? PumpPower(electrical.Value, line) : null;
            double torque = ctx.GetDouble(Station.AiPh("Zone" + zone + "_Torque") + "Pv"), speed = ctx.GetDouble(Station.AiPh("Zone" + zone + "_Speed") + "Pv");
            double? shaft = ShaftPower(torque, speed);

            return new PumpReading
            {
                Flow = flow,
                Head = head,
                DischargeHead = DischargeHead(pd),
                SuctionHead = SuctionHead(ps, line),
                VelocityHead = velocity,
                HydraulicPower = hydraulic,
                ElectricalPower = electrical,
                PumpPower = pump,
                Efficiency = Efficiency(hydraulic, pump),
                Source = electrical.HasValue ? source : 0,
                Voltage = electrical.HasValue ? e.Voltage : 0.0,
                Current = electrical.HasValue ? e.Current : 0.0,
                PowerFactor = electrical.HasValue ? Math.Abs(e.PowerFactor) : 0.0,
                ElectricalFromDrive = electrical.HasValue && e.FromDrive,
                Torque = torque,
                Speed = speed,
                ShaftPower = shaft,
                ShaftEfficiency = Efficiency(hydraulic, PumpShaftPower(shaft, line)),
            };
        }

        /// <summary>
        /// An efficiency entered in % (95) as a factor (0.95). A value up to 1 is taken as a factor already (0.95, as the
        /// report holds it), and an empty, zero or over-100 entry counts as no loss (1).
        /// </summary>
        public static double EfficiencyFactor(string text)
        {
            double v = FirstNumber(text);
            return v <= 0 || v > 100 ? 1.0 : v > 1 ? v / 100.0 : v;
        }

        /// <summary>The first number in a text such as "DN200" or "200 mm"; 0 when there is none.</summary>
        public static double FirstNumber(string text, bool signed = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var m = Regex.Match(text.Replace(',', '.'), signed ? @"-?\d+(\.\d+)?" : @"\d+(\.\d+)?");
            return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
