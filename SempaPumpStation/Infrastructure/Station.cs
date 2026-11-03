using System.Collections.Generic;
using System.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>A power source of the central cabinet. Its id is the source number used by the PLC (1..8).</summary>
    public sealed class PowerSourceInfo
    {
        public int Id;
        public string Name;        // "37 kW VFD"
        public string Device;      // device name inside a zone: "K37Vfd" / "Q630Vfd"
        public string Drive;       // drive name in the IO list: "37Vfd"
        public double RatedKw;
        public bool IsVfd;
        public bool IsMagneticSwitch => Id >= 7;
    }

    /// <summary>An instrument position (an analog input place) that a library device can be mounted on.</summary>
    public sealed class InstrumentPositionInfo
    {
        public int Index;          // 1..23, the PLC's position number
        public string Name;        // "Shared_FM1", "Zone2_Pt3", "Zone1_Vacuummeter"
        public int Category;       // 1 flowmeter, 2 pressure transmitter, 3 vacuummeter
        public int[] Zones;        // the zones that measure with this position
        public string Label;       // operator label: "FM1", "Pt3", "Vakum"
        public string Channel => Name;   // the analog input channel has the same name
    }

    /// <summary>
    /// The fixed layout of the MainPLC station program (FB_Station_Main / FB_Station_Instruments):
    /// sources, zones, instrument positions, analog input channels and the tag naming of the
    /// StationHp / StationPh / StationSamples / StationInstruments / Maintenance DBs.
    /// </summary>
    public static class Station
    {
        public const int ZoneCount = 3;
        public const int SourceCount = 8;
        public const int SampleCapacity = 50;
        public const int LibrarySize = 100;
        public const int SharedFlowmeterCount = 5;
        public const int Zone3FlowmeterCount = 3;
        public const int TransmitterCount = 4;

        public const int Flowmeter = 1;
        public const int PressureTransmitter = 2;
        public const int Vacuummeter = 3;

        // FB_Station_Zone power states
        public const int StateIdle = 0, StateOpening = 1, StateClosing = 2, StateConnected = 3, StateFault = 9;

        public static readonly PowerSourceInfo[] Sources =
        {
            new PowerSourceInfo { Id = 1, Name = "7.5 kW Soft Starter", Device = "K7k5Ss",  Drive = "7k5Ss",  RatedKw = 7.5 },
            new PowerSourceInfo { Id = 2, Name = "7.5 kW VFD",          Device = "K7k5Vfd", Drive = "7k5Vfd", RatedKw = 7.5, IsVfd = true },
            new PowerSourceInfo { Id = 3, Name = "37 kW Soft Starter",  Device = "K37Ss",   Drive = "37Ss",   RatedKw = 37 },
            new PowerSourceInfo { Id = 4, Name = "37 kW VFD",           Device = "K37Vfd",  Drive = "37Vfd",  RatedKw = 37, IsVfd = true },
            new PowerSourceInfo { Id = 5, Name = "110 kW Soft Starter", Device = "K110Ss",  Drive = "110Ss",  RatedKw = 110 },
            new PowerSourceInfo { Id = 6, Name = "110 kW VFD",          Device = "K110Vfd", Drive = "110Vfd", RatedKw = 110, IsVfd = true },
            new PowerSourceInfo { Id = 7, Name = "560 kW Soft Starter", Device = "Q560Ss",  Drive = "560Ss",  RatedKw = 560 },
            new PowerSourceInfo { Id = 8, Name = "630 kW VFD",          Device = "Q630Vfd", Drive = "630Vfd", RatedKw = 630, IsVfd = true },
        };

        public static PowerSourceInfo Source(int id) => id >= 1 && id <= SourceCount ? Sources[id - 1] : null;

        public static readonly string[] TransmitterNames = { "Pt1", "Pt2", "Pt3", "Pt4" };

        /// <summary>The 23 instrument positions in the PLC's order (the Assigned array index).</summary>
        public static readonly InstrumentPositionInfo[] Positions = BuildPositions();

        private static InstrumentPositionInfo[] BuildPositions()
        {
            var list = new List<InstrumentPositionInfo>();
            void Add(string name, int category, string label, params int[] zones) =>
                list.Add(new InstrumentPositionInfo { Index = list.Count + 1, Name = name, Category = category, Label = label, Zones = zones });

            for (int f = 1; f <= SharedFlowmeterCount; f++) Add("Shared_FM" + f, Flowmeter, "FM" + f, 1, 2);
            for (int f = 1; f <= Zone3FlowmeterCount; f++) Add("Zone3_FM" + f, Flowmeter, "FM" + f, 3);
            for (int z = 1; z <= ZoneCount; z++)
                for (int t = 1; t <= TransmitterCount; t++) Add("Zone" + z + "_Pt" + t, PressureTransmitter, "Pt" + t, z);
            for (int z = 1; z <= ZoneCount; z++) Add("Zone" + z + "_Vacuummeter", Vacuummeter, "Vakum", z);
            return list.ToArray();
        }

        public static InstrumentPositionInfo Position(string name) => Positions.FirstOrDefault(p => p.Name == name);

        /// <summary>The flowmeter positions a zone measures with, in selection order (flowmeter 1..n).</summary>
        public static string[] FlowmeterPositions(int zone) =>
            zone == 3 ? new[] { "Zone3_FM1", "Zone3_FM2", "Zone3_FM3" }
                      : new[] { "Shared_FM1", "Shared_FM2", "Shared_FM3", "Shared_FM4", "Shared_FM5" };

        public static int FlowmeterCount(int zone) => zone == 3 ? Zone3FlowmeterCount : SharedFlowmeterCount;

        public static string TransmitterPosition(int zone, int t) => "Zone" + zone + "_Pt" + t;
        public static string VacuummeterPosition(int zone) => "Zone" + zone + "_Vacuummeter";

        /// <summary>Per-zone analog sensors besides the instrument positions.</summary>
        public static readonly string[] ZoneSensors = { "WaterTemperature", "AmbientTemperature", "Vibration", "Temperature", "Sound", "Speed" };

        /// <summary>Every analog input channel of the "Io".AnalogInputs collection.</summary>
        public static IEnumerable<string> AnalogInputs()
        {
            for (int z = 1; z <= ZoneCount; z++)
            {
                for (int t = 1; t <= TransmitterCount; t++) yield return TransmitterPosition(z, t);
                yield return VacuummeterPosition(z);
                foreach (var s in ZoneSensors) yield return "Zone" + z + "_" + s;
            }
            for (int f = 1; f <= SharedFlowmeterCount; f++) yield return "Shared_FM" + f;
            for (int f = 1; f <= Zone3FlowmeterCount; f++) yield return "Zone3_FM" + f;
        }

        public static string CategoryName(int category) =>
            category == Flowmeter ? "Debimetre" : category == PressureTransmitter ? "Basınç Transmitteri" : "Vakummetre";

        public static string LibraryArray(int category) =>
            category == Flowmeter ? "Flowmeters" : category == PressureTransmitter ? "PressureTransmitters" : "Vacuummeters";

        public static string PowerStateText(int state)
        {
            switch (state)
            {
                case StateIdle: return "Boşta";
                case StateOpening: return "Açılıyor";
                case StateClosing: return "Kapanıyor";
                case StateConnected: return "Bağlı";
                case StateFault: return "Arıza";
                default: return "—";
            }
        }

        // ---------------------------------------------------------------- tag names
        public static string ZoneHp(int zone) => "StationHp.Zone" + zone + ".";
        public static string ZonePh(int zone) => "StationPh.Zone" + zone + ".";
        public static string PositionHp(string position) => "StationHp.Instruments." + position + ".";
        public static string PositionPh(string position) => "StationPh.Instruments." + position + ".";
        public static string AiHp(string channel) => "MaintenanceHp.Io.AnalogInputs." + channel + ".";
        public static string AiPh(string channel) => "MaintenancePh.Io.AnalogInputs." + channel + ".";
        public static string Samples(int zone) => "StationSamples.Zone[" + zone + "].";
        public static string Sample(int zone, int index) => Samples(zone) + "Samples[" + index + "].";
        public static string LibraryEntry(int category, int device) => "StationInstruments." + LibraryArray(category) + "[" + device + "].";
        public static string SimZone(int zone) => "Sim.Zone" + zone + ".";
        public static string SimDrive(int source) => "Sim.Drive" + source + ".";

        /// <summary>Selection parameters of a zone (FB_Station_ZoneHmi): name and whether it is Real.</summary>
        public static readonly string[] ZoneIntParameters = { "PowerSource", "Flowmeter", "DischargeTransmitter" };
        public static readonly string[] ZoneButtons = { "Stop", "ResetFault", "StartTest", "EndTest", "DeleteLastSample", "ClearSamples" };

        /// <summary>The analog input parameters that FC_Station_LoadInstrument copies from the library.</summary>
        public static readonly string[] AiRealParameters = { "MinPv", "MaxPv", "FilterEmaAlpha", "FilterOutlierMaxJump" };
        public static readonly string[] AiIntParameters = { "MinRaw", "MaxRaw", "FilterType", "FilterOutlierMaxRejectCount" };

        /// <summary>st_Station_Sample members, in declaration order, with the tag data kind (R real, I int, B bool, S string).</summary>
        public static readonly (string Name, char Kind)[] SampleFields =
        {
            ("Timestamp", 'D'), ("PowerSource", 'I'), ("Flowmeter", 'I'), ("DischargeTransmitter", 'I'),
            ("FlowmeterDevice", 'I'), ("DischargeTransmitterDevice", 'I'), ("VacuummeterDevice", 'I'),
            ("Flow", 'R'), ("DischargePressure", 'R'), ("SuctionPressure", 'R'),
            ("WaterTemperature", 'R'), ("AmbientTemperature", 'R'), ("Vibration", 'R'), ("Temperature", 'R'),
            ("Sound", 'R'), ("Speed", 'R'), ("Voltage", 'R'), ("Current", 'R'), ("SpeedReference", 'R'), ("PumpRunning", 'B'),
        };

        /// <summary>st_Station_Instrument members with the tag data kind.</summary>
        public static readonly (string Name, char Kind)[] InstrumentFields =
        {
            ("Description", 'S'), ("SerialNumber", 'S'), ("Unit", 'S'),
            ("MinRaw", 'I'), ("MaxRaw", 'I'), ("MinPv", 'R'), ("MaxPv", 'R'),
            ("FilterType", 'I'), ("FilterEmaAlpha", 'R'), ("FilterOutlierMaxJump", 'R'), ("FilterOutlierMaxRejectCount", 'I'),
        };
    }
}
