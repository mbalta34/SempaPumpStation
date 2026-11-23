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
        public int Index;          // 1..20, the PLC's position number
        public string Name;        // "Zone2_FM1", "Zone2_Pt2", "Zone1_Vacuummeter1"
        public int Category;       // 1 flowmeter, 2 pressure transmitter, 3 vacuummeter
        public int[] Zones;        // the zones that measure with this position
        public string Label;       // operator label: "FM1", "Pt2", "Vakum1"
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
        /// <summary>
        /// Instruments of each zone (IO list V1.9): zones 2 and 3 have two discharge transmitters, two vacuummeters and four
        /// flowmeters on their BNI00KM; zone 1 one transmitter, one vacuummeter and two flowmeters on its BNI0007.
        /// </summary>
        public const int ZoneFlowmeterCount = 4;
        public const int Zone1FlowmeterCount = 2;
        public const int ZoneTransmitterCount = 2;
        public const int Zone1TransmitterCount = 1;
        public const int ZoneVacuummeterCount = 2;
        public const int Zone1VacuummeterCount = 1;
        /// <summary>The size of the PLC's per-zone transmitter / vacuummeter arrays (st_Station_ZoneInputs).</summary>
        public const int MaxTransmitterCount = 2, MaxVacuummeterCount = 2;

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

        /// <summary>The power device of a source inside a zone: "37 kW VFD kontaktörü", "630 kW VFD şalteri".</summary>
        public static string DeviceText(int source)
        {
            var s = Source(source);
            return s == null ? "" : s.Name + (s.IsMagneticSwitch ? " şalteri" : " kontaktörü");
        }

        /// <summary>The 20 instrument positions in the PLC's order (the Assigned array index, FB_Station_Instruments).</summary>
        public static readonly InstrumentPositionInfo[] Positions = BuildPositions();

        private static InstrumentPositionInfo[] BuildPositions()
        {
            var list = new List<InstrumentPositionInfo>();
            void Add(string name, int category, string label, params int[] zones) =>
                list.Add(new InstrumentPositionInfo { Index = list.Count + 1, Name = name, Category = category, Label = label, Zones = zones });

            for (int f = 1; f <= FlowmeterCount(2); f++) Add("Zone2_FM" + f, Flowmeter, "FM" + f, 2);
            for (int f = 1; f <= FlowmeterCount(3); f++) Add("Zone3_FM" + f, Flowmeter, "FM" + f, 3);
            for (int z = 1; z <= ZoneCount; z++)
                for (int t = 1; t <= TransmitterCount(z); t++) Add(TransmitterPosition(z, t), PressureTransmitter, "Pt" + t, z);
            for (int z = 1; z <= ZoneCount; z++)
                for (int v = 1; v <= VacuummeterCount(z); v++) Add(VacuummeterPosition(z, v), Vacuummeter, "Vakum" + v, z);
            for (int f = 1; f <= FlowmeterCount(1); f++) Add("Zone1_FM" + f, Flowmeter, "FM" + f, 1);
            return list.ToArray();
        }

        public static InstrumentPositionInfo Position(string name) => Positions.FirstOrDefault(p => p.Name == name);

        /// <summary>The flowmeter positions a zone measures with, in selection order (flowmeter 1..n).</summary>
        public static string[] FlowmeterPositions(int zone) =>
            Enumerable.Range(1, FlowmeterCount(zone)).Select(f => "Zone" + zone + "_FM" + f).ToArray();

        public static int FlowmeterCount(int zone) => zone == 1 ? Zone1FlowmeterCount : ZoneFlowmeterCount;

        public static int TransmitterCount(int zone) => zone == 1 ? Zone1TransmitterCount : ZoneTransmitterCount;
        public static int VacuummeterCount(int zone) => zone == 1 ? Zone1VacuummeterCount : ZoneVacuummeterCount;

        public static string TransmitterPosition(int zone, int t) => "Zone" + zone + "_Pt" + t;
        public static string VacuummeterPosition(int zone, int v) => "Zone" + zone + "_Vacuummeter" + v;

        /// <summary>Per-zone analog sensors besides the instrument positions.</summary>
        public static readonly string[] ZoneSensors = { "WaterTemperature", "AmbientTemperature", "Vibration", "Temperature", "Sound", "Speed", "Torque" };

        /// <summary>Every analog input channel of the "Io".AnalogInputs collection.</summary>
        public static IEnumerable<string> AnalogInputs()
        {
            for (int z = 1; z <= ZoneCount; z++)
            {
                for (int t = 1; t <= TransmitterCount(z); t++) yield return TransmitterPosition(z, t);
                for (int v = 1; v <= VacuummeterCount(z); v++) yield return VacuummeterPosition(z, v);
                foreach (var s in ZoneSensors) yield return "Zone" + z + "_" + s;
            }
            for (int z = 1; z <= ZoneCount; z++)
                foreach (var p in FlowmeterPositions(z)) yield return p;
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
        /// <summary>"StationCounters".NextTestNo: the retained number the next started test gets.</summary>
        public const string NextTestNo = "StationCounters.NextTestNo";
        public static string Sample(int zone, int index) => Samples(zone) + "Samples[" + index + "].";
        public static string LibraryEntry(int category, int device) => "StationInstruments." + LibraryArray(category) + "[" + device + "].";
        /// <summary>The same device's coefficient, offset and moving average ("StationInstrumentAdjustments", kept apart from the library DB).</summary>
        public static string AdjustmentEntry(int category, int device) => "StationInstrumentAdjustments." + LibraryArray(category) + "[" + device + "].";
        public static string SimZone(int zone) => "Sim.Zone" + zone + ".";
        public static string SimDrive(int source) => "Sim.Drive" + source + ".";
        /// <summary>The PM5550 on a power source ("Powermeters".Meter[s]: decoded by FB_Communication_Pm5550Decoder, filtered by FB_Communication_PowermeterFilter).</summary>
        public static string Meter(int source) => "Powermeters.Meter[" + source + "].";
        /// <summary>The filter settings of a power source's meter (FB_Communication_PowermeterFilterHmi).</summary>
        public static string MeterFilterHp(int source) => "StationHp.Powermeters[" + source + "].";
        public static string MeterFilterPh(int source) => "StationPh.Powermeters[" + source + "].";

        // ---------------------------------------------------------------- drive
        /// <summary>The power source whose drive is read over Modbus: the ATV660 of the 630 kW VFD.</summary>
        public const int DriveSource = 8;
        /// <summary>Its values ("Drives".Vfd630, FB_Communication_Atv600Decoder; read only).</summary>
        public const string Drive630 = "Drives.Vfd630.";
        /// <summary>The drive's filter settings (FB_Communication_DriveFilter; the same parameters as a meter's).</summary>
        public const string DriveFilterHp = "StationHp.Drive630.", DriveFilterPh = "StationPh.Drive630.";
        /// <summary>st_Instruments_DriveData members with the tag data kind.</summary>
        public static readonly (string Name, char Kind)[] DriveFields =
        {
            ("CommOk", 'B'), ("StatusWord", 'I'), ("OutputFrequency", 'R'), ("MotorCurrent", 'R'), ("MotorTorque", 'R'),
            ("MainsVoltage", 'R'), ("MotorVoltage", 'R'), ("MotorPower", 'R'), ("ExtraCommOk", 'B'), ("MotorTorqueNm", 'R'),
            ("MotorMechanicalPower", 'R'), ("OutputElectricalPower", 'R'),
        };

        // ---------------------------------------------------------------- power meters
        /// <summary>The value groups of a meter (st_Instruments_PowermeterData).</summary>
        /// <remarks>The station is a 3-wire system: the phases carry the line-to-line voltages (A-B, B-C, C-A) and the phase currents only.</remarks>
        public static readonly string[] MeterGroups = { "PhaseA", "PhaseB", "PhaseC", "General" };
        /// <summary>The values of each group (st_Instruments_PowermeterPhaseData).</summary>
        public static readonly string[] MeterValues =
            { "Voltage", "Current", "ActivePower", "ReactivePower", "ApparentPower", "PowerFactor", "ActiveEnergy", "ReactiveEnergy", "ApparentEnergy", "Frequency" };
        /// <summary>
        /// The PM5550's energy registers count in Wh (PM5500 register list, Float32 energies from 2700): the
        /// PLC passes them on unchanged, the HMI shows kWh.
        /// </summary>
        public const double MeterEnergyPerKwh = 1000.0;

        /// <summary>A meter's filter parameters (st_Communication_*_PowermeterFilter), all Int except the EMA weight.</summary>
        public static readonly string[] MeterFilterIntParameters = { "FilterType", "FilterAverageCount", "FilterOutlierMaxRejectCount" };
        public static readonly string[] MeterFilterRealParameters = { "FilterEmaAlpha", "FilterOutlierMaxJump" };

        /// <summary>Selection parameters of a zone (FB_Station_ZoneHmi): name and whether it is Real.</summary>
        public static readonly string[] ZoneIntParameters = { "PowerSource", "Flowmeter", "DischargeTransmitter", "Vacuummeter" };
        public static readonly string[] ZoneButtons = { "Stop", "ResetFault", "StartTest", "EndTest", "DeleteLastSample", "ClearSamples", "RotationTest", "ConfirmRotation" };

        // Rotation test (FB_Station_Zone ROTATION_*, "StationPh".ZoneN.RotationState)
        public const int RotationNone = 0, RotationRunning = 1, RotationWaitConfirm = 2, RotationConfirmed = 3;

        public static string RotationStateText(int state)
        {
            switch (state)
            {
                case RotationRunning: return "Yön testi sürüyor";
                case RotationWaitConfirm: return "Dönüş yönünü onaylayın";
                case RotationConfirmed: return "Yön onaylandı";
                default: return "Yön kontrol edilmedi";
            }
        }

        /// <summary>The analog input parameters that FC_Station_LoadInstrument copies from the library (every cycle).</summary>
        public static readonly string[] AiRealParameters = { "MinPv", "MaxPv", "FilterEmaAlpha", "FilterOutlierMaxJump" };
        public static readonly string[] AiIntParameters = { "MinRaw", "MaxRaw", "FilterType", "FilterOutlierMaxRejectCount" };

        /// <summary>
        /// Pv = scaled × Coefficient + Offset. A fixed sensor's own parameters (Analog Ayarları); on an instrument position they
        /// come from the mounted device's <see cref="AdjustmentEntry"/>, the same names.
        /// </summary>
        public static readonly string[] AiChannelRealParameters = { "Coefficient", "Offset" };
        /// <summary>The channel's own moving average (FilterAverageCount values, one per OB30 cycle; 0 or 1 = off).</summary>
        public static readonly string[] AiChannelIntParameters = { "FilterAverageCount" };

        /// <summary>
        /// The signal type the PLC program passes to a channel (FB_Station_Instruments → FB_Io_AnalogInput.signalType):
        /// torque −10..10 V, speed 0..10 V, every other channel the default 4..20 mA.
        /// </summary>
        public static int SignalType(string channel)
        {
            if (channel.EndsWith("_Torque", System.StringComparison.Ordinal)) return AnalogSignal.VoltageMinus10To10;
            if (channel.EndsWith("_Speed", System.StringComparison.Ordinal)) return AnalogSignal.Voltage0To10;
            return AnalogSignal.Current4To20;
        }

        /// <summary>st_Station_Sample members, in declaration order, with the tag data kind (R real, I int, B bool, S string).</summary>
        public static readonly (string Name, char Kind)[] SampleFields =
        {
            ("Timestamp", 'D'), ("PowerSource", 'I'), ("Flowmeter", 'I'), ("DischargeTransmitter", 'I'), ("Vacuummeter", 'I'),
            ("FlowmeterDevice", 'I'), ("DischargeTransmitterDevice", 'I'), ("VacuummeterDevice", 'I'),
            ("Flow", 'R'), ("DischargePressure", 'R'), ("SuctionPressure", 'R'),
            ("WaterTemperature", 'R'), ("AmbientTemperature", 'R'), ("Vibration", 'R'), ("Temperature", 'R'),
            ("Sound", 'R'), ("Speed", 'R'), ("Voltage", 'R'), ("Current", 'R'), ("SpeedReference", 'R'), ("PumpRunning", 'B'),
            ("Torque", 'R'), ("PowerFactor", 'R'),
        };

        /// <summary>st_Station_Instrument members with the tag data kind.</summary>
        /// <summary>st_Station_InstrumentAdjustment members with the tag data kind.</summary>
        public static readonly (string Name, char Kind)[] AdjustmentFields = { ("Coefficient", 'R'), ("Offset", 'R'), ("FilterAverageCount", 'I') };

        public static readonly (string Name, char Kind)[] InstrumentFields =
        {
            ("Description", 'S'), ("SerialNumber", 'S'), ("Unit", 'S'),
            ("MinRaw", 'I'), ("MaxRaw", 'I'), ("MinPv", 'R'), ("MaxPv", 'R'),
            ("FilterType", 'I'), ("FilterEmaAlpha", 'R'), ("FilterOutlierMaxJump", 'R'), ("FilterOutlierMaxRejectCount", 'I'),
        };
    }
}
