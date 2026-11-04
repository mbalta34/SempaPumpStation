using System.Collections.Generic;
using System.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>One digital input of the "Io".DigitalInputs collection.</summary>
    public sealed class DigitalInputInfo
    {
        /// <summary>Channel name in the PLC, e.g. "Zone1_K37Vfd_Closed".</summary>
        public string Name;
        /// <summary>1..3, or 0 for the drive feedbacks of the central cabinet.</summary>
        public int Zone;
        public string Label;

        public string Ph => "MaintenancePh.Io.DigitalInputs." + Name + ".";
    }

    /// <summary>
    /// The 82 digital inputs of MainPLC in the order of st_Io_*_DigitalInputs (New-IoCollections.ps1): per zone the
    /// contactor / switch feedbacks, the pendant and the E-stop, then the running and fault feedback of each drive.
    /// </summary>
    public static class DigitalInputs
    {
        public static readonly DigitalInputInfo[] All = Build();

        public static IEnumerable<DigitalInputInfo> OfZone(int zone) => All.Where(i => i.Zone == zone);

        private static DigitalInputInfo[] Build()
        {
            var list = new List<DigitalInputInfo>();
            void Add(string name, int zone, string label) => list.Add(new DigitalInputInfo { Name = name, Zone = zone, Label = label });

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string zone = "Zone" + z + "_";
                foreach (var s in Station.Sources)
                {
                    string device = s.Name + (s.IsMagneticSwitch ? " şalteri" : " kontaktörü");
                    Add(zone + s.Device + "_Open", z, device + " açık");
                    Add(zone + s.Device + "_Closed", z, device + " kapalı");
                    if (s.IsMagneticSwitch) Add(zone + s.Device + "_SpringCharged", z, s.Name + " şalter yayı kurulu");
                }
                Add(zone + "PendantStart", z, "Pandant START");
                Add(zone + "PendantStop", z, "Pandant STOP");
                Add(zone + "PendantTakeSample", z, "Pandant NUMUNE AL");
                Add(zone + "EStopOk", z, "Acil stop sağlam");
            }
            foreach (var s in Station.Sources)
            {
                Add("Drive_" + s.Drive + "_Running", 0, s.Name + " çalışıyor");
                Add("Drive_" + s.Drive + "_Fault", 0, s.Name + " arıza");
            }
            return list.ToArray();
        }
    }
}
