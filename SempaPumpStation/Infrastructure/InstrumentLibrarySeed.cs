using System.Collections.Generic;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The demo content of the retained PLC data the HMI would find on a commissioned station:
    /// the first entries of the StationInstruments library and the devices mounted on the positions.
    /// </summary>
    public static class InstrumentLibrarySeed
    {
        private sealed class Entry
        {
            public string Description, Serial, Unit;
            public double MinPv, MaxPv;
            public int FilterType;
        }

        private static readonly Entry[] Flowmeters =
        {
            new Entry { Description = "Krohne Optiflux DN100", Serial = "KR-A11872", Unit = "m³/h", MaxPv = 150, FilterType = 1 },
            new Entry { Description = "Krohne Optiflux DN150", Serial = "KR-A11905", Unit = "m³/h", MaxPv = 350, FilterType = 1 },
            new Entry { Description = "Krohne Optiflux DN200", Serial = "KR-A12034", Unit = "m³/h", MaxPv = 650, FilterType = 1 },
            new Entry { Description = "Krohne Optiflux DN250", Serial = "KR-A12118", Unit = "m³/h", MaxPv = 1000, FilterType = 1 },
            new Entry { Description = "Krohne Optiflux DN300", Serial = "KR-A12240", Unit = "m³/h", MaxPv = 1450, FilterType = 1 },
            new Entry { Description = "Siemens Mag5000 DN600", Serial = "SI-7ME6520", Unit = "m³/h", MaxPv = 5000, FilterType = 2 },
            new Entry { Description = "E+H Promag DN100", Serial = "EH-P10-3381", Unit = "m³/h", MaxPv = 150, FilterType = 1 },
            new Entry { Description = "E+H Promag DN200", Serial = "EH-P10-3412", Unit = "m³/h", MaxPv = 650, FilterType = 1 },
        };

        private static readonly Entry[] Transmitters =
        {
            new Entry { Description = "Wika S-20 0-6 bar", Serial = "WK-110045", Unit = "bar", MaxPv = 6 },
            new Entry { Description = "Wika S-20 0-10 bar", Serial = "WK-110102", Unit = "bar", MaxPv = 10 },
            new Entry { Description = "Wika S-20 0-16 bar", Serial = "WK-110188", Unit = "bar", MaxPv = 16 },
            new Entry { Description = "Wika S-20 0-25 bar", Serial = "WK-110231", Unit = "bar", MaxPv = 25 },
            new Entry { Description = "Wika S-20 0-40 bar", Serial = "WK-110290", Unit = "bar", MaxPv = 40 },
            new Entry { Description = "Wika S-20 0-60 bar", Serial = "WK-110344", Unit = "bar", MaxPv = 60 },
            new Entry { Description = "Danfoss MBS 0-16 bar", Serial = "DF-33-8810", Unit = "bar", MaxPv = 16 },
            new Entry { Description = "Danfoss MBS 0-25 bar", Serial = "DF-33-8862", Unit = "bar", MaxPv = 25 },
            new Entry { Description = "Keller PA-21 0-10 bar", Serial = "KE-554120", Unit = "bar", MaxPv = 10 },
            new Entry { Description = "Keller PA-21 0-16 bar", Serial = "KE-554187", Unit = "bar", MaxPv = 16 },
            new Entry { Description = "Keller PA-21 0-25 bar", Serial = "KE-554233", Unit = "bar", MaxPv = 25 },
            new Entry { Description = "Keller PA-21 0-60 bar", Serial = "KE-554301", Unit = "bar", MaxPv = 60 },
        };

        private static readonly Entry[] Vacuummeters =
        {
            new Entry { Description = "Wika S-20 -1..0 bar", Serial = "WK-V20011", Unit = "bar", MinPv = -1, MaxPv = 0 },
            new Entry { Description = "Wika S-20 -1..1.5 bar", Serial = "WK-V20047", Unit = "bar", MinPv = -1, MaxPv = 1.5 },
            new Entry { Description = "Danfoss MBS -1..5 bar", Serial = "DF-V3-4410", Unit = "bar", MinPv = -1, MaxPv = 5 },
            new Entry { Description = "Keller PA-21 -1..3 bar", Serial = "KE-V55410", Unit = "bar", MinPv = -1, MaxPv = 3 },
        };

        /// <summary>Library start values as (tag name, value) pairs.</summary>
        public static IEnumerable<KeyValuePair<string, object>> LibraryValues()
        {
            foreach (var kv in Category(Station.Flowmeter, Flowmeters)) yield return kv;
            foreach (var kv in Category(Station.PressureTransmitter, Transmitters)) yield return kv;
            foreach (var kv in Category(Station.Vacuummeter, Vacuummeters)) yield return kv;
        }

        private static IEnumerable<KeyValuePair<string, object>> Category(int category, Entry[] entries)
        {
            for (int d = 1; d <= Station.LibrarySize; d++)
            {
                string e = Station.LibraryEntry(category, d);
                // Unused entries keep the start values of st_Station_Instrument
                var x = d <= entries.Length ? entries[d - 1] : new Entry { Description = "", Serial = "", Unit = "", MaxPv = 100 };
                yield return Kv(e + "Description", x.Description);
                yield return Kv(e + "SerialNumber", x.Serial);
                yield return Kv(e + "Unit", x.Unit);
                yield return Kv(e + "MinRaw", 0);
                yield return Kv(e + "MaxRaw", 27648);
                yield return Kv(e + "MinPv", x.MinPv);
                yield return Kv(e + "MaxPv", x.MaxPv);
                yield return Kv(e + "FilterType", x.FilterType);
                yield return Kv(e + "FilterEmaAlpha", 0.2);
                yield return Kv(e + "FilterOutlierMaxJump", 1000.0);
                yield return Kv(e + "FilterOutlierMaxRejectCount", 5);
            }
        }

        /// <summary>The device retained on each position (0 = nothing mounted).</summary>
        public static int MountedDevice(InstrumentPositionInfo position)
        {
            switch (position.Name)
            {
                case "Shared_FM1": return 1;
                case "Shared_FM2": return 2;
                case "Shared_FM3": return 3;
                case "Shared_FM4": return 4;
                case "Shared_FM5": return 6;
                case "Zone3_FM1": return 7;
                case "Zone3_FM2": return 8;
                case "Zone3_FM3": return 5;
                case "Zone1_Pt1": return 1;
                case "Zone1_Pt2": return 3;
                case "Zone1_Pt3": return 4;
                case "Zone1_Pt4": return 6;
                case "Zone2_Pt1": return 2;
                case "Zone2_Pt2": return 7;
                case "Zone2_Pt3": return 8;
                case "Zone2_Pt4": return 5;
                case "Zone3_Pt1": return 9;
                case "Zone3_Pt2": return 10;
                case "Zone3_Pt3": return 11;
                case "Zone3_Pt4": return 12;
                case "Zone1_Vacuummeter": return 1;
                case "Zone2_Vacuummeter": return 2;
                case "Zone3_Vacuummeter": return 4;
                default: return 0;
            }
        }

        private static KeyValuePair<string, object> Kv(string k, object v) => new KeyValuePair<string, object>(k, v);
    }
}
