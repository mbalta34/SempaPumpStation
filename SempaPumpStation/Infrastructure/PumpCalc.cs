namespace SempaPumpStation.Infrastructure
{
    /// <summary>The live pump figures of one zone, as the TV screen shows them.</summary>
    public sealed class PumpReading
    {
        public double Flow { get; set; }             // m³/h
        public double Head { get; set; }             // total head, m
        public double HydraulicPower { get; set; }   // kW
        /// <summary>Pp: electrical power taken by the pump set, kW, from the selected source's PM5550; null when unknown.</summary>
        public double? PumpPower { get; set; }
        /// <summary>Overall efficiency (hydraulic over electrical power), %; null when <see cref="PumpPower"/> is unknown or zero.</summary>
        public double? Efficiency { get; set; }
        /// <summary>The source whose meter gave <see cref="PumpPower"/>, 0 = none.</summary>
        public int Source { get; set; }
    }

    /// <summary>
    /// The pump-test formulas, in one place for the live screens and the archived report.
    /// </summary>
    public static class PumpCalc
    {
        /// <summary>Metres of water column per bar.</summary>
        public const double MetresPerBar = 10.197;

        /// <summary>Q [m³/h] × H [m] / 367.2 gives the hydraulic power in kW (water, ρ = 1000 kg/m³).</summary>
        public const double HydraulicPowerDivisor = 367.2;

        /// <summary>
        /// Total head: the pressure difference plus the height difference between the gauges.
        /// A submersible pump has no suction gauge, so suction is ignored.
        /// </summary>
        public static double Head(double dischargeBar, double suctionBar, bool submersible, double geodeticHeight)
            => (dischargeBar - (submersible ? 0.0 : suctionBar)) * MetresPerBar + geodeticHeight;

        public static double HydraulicPower(double flow, double head) => flow * head / HydraulicPowerDivisor;

        /// <summary>Hydraulic power over pump power, in %; null when the pump power is unknown or zero.</summary>
        public static double? Efficiency(double hydraulicPower, double? pumpPower)
            => pumpPower.HasValue && pumpPower.Value > 0 ? hydraulicPower / pumpPower.Value * 100.0 : (double?)null;

        /// <summary>The live figures of a zone from its StationPh values and the line settings.</summary>
        public static PumpReading Live(HmiContext ctx, int zone)
        {
            string ph = Station.ZonePh(zone);
            double flow = ctx.GetDouble(ph + "Flow");
            double head = Head(ctx.GetDouble(ph + "DischargePressure"), ctx.GetDouble(ph + "SuctionPressure"),
                               ctx.GetBool("DALGIC_PMP_SEL"), ctx.GetDouble("GEO_YUKSEKLIK"));
            double hydraulic = HydraulicPower(flow, head);

            // Pp: the active power the PM5550 of the zone's selected source measures (electrical input of the
            // motor or drive). Unknown while no source is selected or its meter does not answer.
            double? pumpPower = null;
            int source = ctx.GetInt(ph + "PowerSource.Value");
            if (source >= 1 && source <= Station.SourceCount && ctx.GetBool(Station.Meter(source) + "CommOk"))
                pumpPower = ctx.GetDouble(Station.Meter(source) + "General.ActivePower");

            return new PumpReading
            {
                Flow = flow,
                Head = head,
                HydraulicPower = hydraulic,
                PumpPower = pumpPower,
                Efficiency = Efficiency(hydraulic, pumpPower),
                Source = pumpPower.HasValue ? source : 0,
            };
        }
    }
}
