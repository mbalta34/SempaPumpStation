using System.Globalization;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The signal types of FB_Io_AnalogInput (its signalType input, SIGNAL_4_20_MA .. SIGNAL_MINUS10_10_V; default
    /// 4..20 mA). The type is set in the PLC program, not on the HMI; the PLC reports it in st_Io_Ph_AnalogInput.SignalType.
    /// The channel's MinRaw / MaxRaw are the raw values at the two ends of the signal range, so the PLC also reports the
    /// raw and filtered values as mA or V (RawSignal / FilteredSignal).
    /// </summary>
    public static class AnalogSignal
    {
        public const int Current4To20 = 1, Current0To20 = 2, Voltage0To10 = 3, VoltageMinus10To10 = 4;

        /// <summary>The signal range of a type (FB_Io_AnalogInput's CASE); 0..0 for an unknown type.</summary>
        public static (double Min, double Max) Range(int type)
        {
            switch (type)
            {
                case Current4To20: return (4.0, 20.0);
                case Current0To20: return (0.0, 20.0);
                case Voltage0To10: return (0.0, 10.0);
                case VoltageMinus10To10: return (-10.0, 10.0);
                default: return (0.0, 0.0);
            }
        }

        public static string Name(int type)
        {
            switch (type)
            {
                case Current4To20: return "4…20 mA";
                case Current0To20: return "0…20 mA";
                case Voltage0To10: return "0…10 V";
                case VoltageMinus10To10: return "−10…10 V";
                default: return "—";
            }
        }

        public static string Unit(int type) =>
            type == Current4To20 || type == Current0To20 ? "mA" : type == Voltage0To10 || type == VoltageMinus10To10 ? "V" : "";

        /// <summary>A signal value with its unit ("12.000 mA", "-3.250 V"); "—" for an unknown type.</summary>
        public static string Format(int type, double value) =>
            Unit(type).Length == 0 ? "—" : value.ToString("0.000", CultureInfo.InvariantCulture) + " " + Unit(type);
    }
}
