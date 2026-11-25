using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SempaPumpStation.Infrastructure.Security;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>What a zone's pump efficiency is calculated from, and so which values its zone and TV screens show.</summary>
    public enum EfficiencyBasis
    {
        /// <summary>Pp = Nş × motor efficiency (report columns M, N, O): U, I, cos φ, Nş, Pp.</summary>
        Electrical = 1,
        /// <summary>Nmil = T × n / 9549 from the torque meter (report columns R, S): torque and shaft power.</summary>
        Torque = 2,
        /// <summary>Both, side by side.</summary>
        Both = 3,
    }

    /// <summary>
    /// Each zone's <see cref="EfficiencyBasis"/>, chosen on its zone screen and kept on this PC (EfficiencyBasis.xml next to
    /// users.xml), so the zone and TV screens come back the same after a restart. A zone without a choice uses the
    /// electrical basis, as the HMI did before the choice.
    /// </summary>
    public static class EfficiencyBasisStore
    {
        private static Dictionary<int, EfficiencyBasis> _zones;
        private static readonly object Sync = new object();

        /// <summary>The settings file; it follows <see cref="StationSecurity.DataFolder"/>, which the smoke test moves.</summary>
        public static string SettingsFile => Path.Combine(StationSecurity.DataFolder, "EfficiencyBasis.xml");

        public static EfficiencyBasis Get(int zone)
        {
            lock (Sync)
            {
                Load();
                return _zones.TryGetValue(zone, out var b) ? b : EfficiencyBasis.Electrical;
            }
        }

        public static void Set(int zone, EfficiencyBasis basis)
        {
            if (!Enum.IsDefined(typeof(EfficiencyBasis), basis)) return;
            lock (Sync)
            {
                Load();
                _zones[zone] = basis;
                Save();
            }
        }

        public static bool ShowsElectrical(EfficiencyBasis b) => b != EfficiencyBasis.Torque;
        public static bool ShowsTorque(EfficiencyBasis b) => b != EfficiencyBasis.Electrical;

        public static string Text(EfficiencyBasis b) =>
            b == EfficiencyBasis.Torque ? "Tork" : b == EfficiencyBasis.Both ? "Elektrik + Tork" : "Elektrik";

        private static void Load()
        {
            if (_zones != null) return;
            _zones = new Dictionary<int, EfficiencyBasis>();
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (var e in XDocument.Load(SettingsFile).Root.Elements("Zone"))
                {
                    int? zone = (int?)e.Attribute("Number");
                    if (zone.HasValue && Enum.TryParse((string)e.Attribute("Basis"), out EfficiencyBasis b) && Enum.IsDefined(typeof(EfficiencyBasis), b))
                        _zones[zone.Value] = b;
                }
            }
            catch (Exception) { /* a damaged file only loses the choices */ }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                new XDocument(new XElement("EfficiencyBasis",
                        _zones.OrderBy(z => z.Key).Select(z => new XElement("Zone", new XAttribute("Number", z.Key), new XAttribute("Basis", z.Value)))))
                    .Save(SettingsFile);
            }
            catch (Exception) { /* not saved: the choice holds until the HMI closes */ }
        }

        /// <summary>Forgets the loaded choices, so the next read takes the file again (the smoke test).</summary>
        internal static void Reload()
        {
            lock (Sync) _zones = null;
        }
    }
}
