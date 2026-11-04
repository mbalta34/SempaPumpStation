using System.Collections.Generic;
using System.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>One digital output of the "Io".DigitalOutputs collection.</summary>
    public sealed class DigitalOutputInfo
    {
        /// <summary>Channel name in the PLC, e.g. "Zone1_K37Vfd_Close".</summary>
        public string Name;
        /// <summary>1..3, or 0 for the drive outputs of the central cabinet.</summary>
        public int Zone;
        public string Label;

        public string Hp => "MaintenanceHp.Io.DigitalOutputs." + Name + ".";
        public string Ph => "MaintenancePh.Io.DigitalOutputs." + Name + ".";
    }

    /// <summary>
    /// The 53 digital outputs of MainPLC in the order of st_Io_*_DigitalOutputs (New-IoCollections.ps1): per zone the
    /// six contactors, the two magnetic switches and the column lamps, then the eight drive run commands.
    /// </summary>
    public static class DigitalOutputs
    {
        public static readonly DigitalOutputInfo[] All = Build();

        public static IEnumerable<DigitalOutputInfo> OfZone(int zone) => All.Where(o => o.Zone == zone);

        private static DigitalOutputInfo[] Build()
        {
            var list = new List<DigitalOutputInfo>();
            void Add(string name, int zone, string label) => list.Add(new DigitalOutputInfo { Name = name, Zone = zone, Label = label });

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string zone = "Zone" + z + "_";
                foreach (var s in Station.Sources.Where(s => !s.IsMagneticSwitch))
                    Add(zone + s.Device + "_Close", z, s.Name + " kontaktörü kapat");
                foreach (var s in Station.Sources.Where(s => s.IsMagneticSwitch))
                {
                    Add(zone + s.Device + "_Open", z, s.Name + " şalteri aç");
                    Add(zone + s.Device + "_Close", z, s.Name + " şalteri kapat");
                    Add(zone + s.Device + "_ChargeSpring", z, s.Name + " şalter yayını kur");
                }
                Add(zone + "LampGreen", z, "Yeşil ışık");
                Add(zone + "LampYellow", z, "Sarı ışık");
                Add(zone + "LampRed", z, "Kırmızı ışık");
            }
            foreach (var s in Station.Sources)
                Add("Drive_" + s.Drive + "_Run", 0, s.Name + " çalıştır");
            return list.ToArray();
        }
    }
}
