using System.Collections.Generic;
using System.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>One analog input of the "Io".AnalogInputs collection.</summary>
    public sealed class AnalogInputInfo
    {
        /// <summary>Channel name in the PLC, e.g. "Zone2_Pt3".</summary>
        public string Name;
        public string Group;
        public string Label;
        /// <summary>Unit of a fixed sensor; empty for an instrument position (its mounted library device has the unit).</summary>
        public string Unit = "";
        /// <summary>The instrument position this channel belongs to, or null.</summary>
        public InstrumentPositionInfo Position;

        public string Ph => Station.AiPh(Name);
    }

    /// <summary>The analog inputs grouped as on the station: per zone, and the flowmeters zones 1 and 2 share.</summary>
    public static class AnalogInputList
    {
        public const string SharedGroup = "ORTAK DEBİMETRELER (BÖLGE 1–2)";

        private static readonly Dictionary<string, (string Label, string Unit)> Sensors = new Dictionary<string, (string, string)>
        {
            { "WaterTemperature", ("Su sıcaklığı", "°C") },
            { "AmbientTemperature", ("Ortam sıcaklığı", "°C") },
            { "Vibration", ("Titreşim", "mm/s") },
            { "Temperature", ("Motor sıcaklığı", "°C") },
            { "Sound", ("Ses seviyesi", "dB") },
            { "Speed", ("Devir", "d/dk") },
        };

        public static readonly AnalogInputInfo[] All = Build();

        public static IEnumerable<string> Groups => All.Select(a => a.Group).Distinct();

        private static AnalogInputInfo[] Build()
        {
            var list = new List<AnalogInputInfo>();
            foreach (var name in Station.AnalogInputs())
            {
                var position = Station.Position(name);
                var info = new AnalogInputInfo { Name = name, Position = position };
                if (position != null)
                {
                    info.Group = position.Zones.Length > 1 ? SharedGroup : "BÖLGE " + position.Zones[0];
                    info.Label = position.Category == Station.Vacuummeter ? "Vakummetre (emme)"
                               : position.Category == Station.PressureTransmitter ? position.Label + " basma transmitteri"
                               : position.Label + " debimetre";
                }
                else
                {
                    var (label, unit) = Sensors[name.Substring(name.IndexOf('_') + 1)];
                    info.Group = "BÖLGE " + name.Substring(4, 1);
                    info.Label = label;
                    info.Unit = unit;
                }
                list.Add(info);
            }
            // Zones in order, the shared flowmeters last
            return list.OrderBy(a => a.Group == SharedGroup ? 9 : 0).ThenBy(a => a.Group).ToArray();
        }
    }
}
