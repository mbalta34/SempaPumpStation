using System.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>One analog output of the "Io".AnalogOutputs collection.</summary>
    public sealed class AnalogOutputInfo
    {
        /// <summary>Channel name in the PLC, e.g. "Drive_37Vfd_SpeedReference".</summary>
        public string Name;
        public string Label;
        /// <summary>The power source whose drive the output feeds.</summary>
        public PowerSourceInfo Source;

        public string Ph => "MaintenancePh.Io.AnalogOutputs." + Name + ".";
    }

    /// <summary>The analog outputs of MainPLC: the speed reference of each VFD, in the order of st_Io_*_AnalogOutputs.</summary>
    public static class AnalogOutputList
    {
        public static readonly AnalogOutputInfo[] All = Station.Sources.Where(s => s.IsVfd)
            .Select(s => new AnalogOutputInfo { Name = "Drive_" + s.Drive + "_SpeedReference", Label = s.Name + " hız referansı", Source = s })
            .ToArray();
    }
}
