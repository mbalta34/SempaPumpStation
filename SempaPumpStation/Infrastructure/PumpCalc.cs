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
        /// <summary>He: suction head, m, positive under vacuum (the report's He in bar × 10.2); 0 for a submersible pump.</summary>
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
        /// <summary>The source whose meter gave <see cref="ElectricalPower"/>, 0 = none.</summary>
        public int Source { get; set; }
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
        /// <summary>Plant and mechanical efficiency factors (report M10, M11): 1 in the template.</summary>
        public double PlantEfficiency = 1.0, MechanicalEfficiency = 1.0;

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

        /// <summary>He, the suction head in m, positive under vacuum (report: He = −Ps [bar] × F16); 0 for a submersible pump.</summary>
        public static double SuctionHead(double suctionBar, PumpLine line) => line.Submersible ? 0.0 : -suctionBar * MetresPerBar;

        /// <summary>
        /// Total head (report column I): Hb + He, the velocity head difference and the height between the gauges. A
        /// submersible pump has no suction gauge, so suction is ignored.
        /// </summary>
        public static double Head(double dischargeBar, double suctionBar, double flow, PumpLine line)
            => DischargeHead(dischargeBar) + SuctionHead(suctionBar, line) + VelocityHead(flow, line) + line.Geodetic;

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

        /// <summary>Pump efficiency in % (report column O): hydraulic power over pump power; null when that is unknown or zero.</summary>
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

            // Nş from the PM5550 of the zone's selected source, as each sample stores it (U, I, cos φ); unknown while
            // no source is selected or its meter does not answer
            double? electrical = null;
            int source = ctx.GetInt(ph + "PowerSource.Value");
            if (source >= 1 && source <= Station.SourceCount && ctx.GetBool(Station.Meter(source) + "CommOk"))
            {
                string m = Station.Meter(source) + "General.";
                electrical = ElectricalPower(ctx.GetDouble(m + "Voltage"), ctx.GetDouble(m + "Current"), ctx.GetDouble(m + "PowerFactor"));
            }
            double? pump = electrical.HasValue ? PumpPower(electrical.Value, line) : null;

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
            };
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
