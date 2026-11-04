using System;
using System.IO;
using System.Xml.Linq;
using TerakkiFramework.Config;
using TerakkiFramework.Logging;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// config.xml next to the exe, in RailHMI's format (read through <see cref="GlobalConfig"/>: InputMode,
    /// IpAddress, Id) plus this HMI's own elements:
    /// <code>
    /// &lt;Backend&gt;Simulation | Plc&lt;/Backend&gt;   default Simulation
    /// &lt;Rack&gt;0&lt;/Rack&gt; &lt;Slot&gt;1&lt;/Slot&gt;              S7-1500 CPU: 0 / 1
    /// &lt;AutoLogoutMinutes&gt;15&lt;/AutoLogoutMinutes&gt;      logout after this long without input, 0 = never
    /// &lt;PasswordMaxAgeDays&gt;90&lt;/PasswordMaxAgeDays&gt;    password must be changed after this, 0 = never
    /// </code>
    /// </summary>
    public sealed class StationConfig
    {
        public bool UsePlc { get; private set; }
        public string IpAddress { get; private set; }
        public int Rack { get; private set; }
        public int Slot { get; private set; } = 1;
        public TimeSpan AutoLogout { get; private set; } = TimeSpan.FromMinutes(15);
        /// <summary>Null keeps the framework's <see cref="TerakkiFramework.Security.SecurityOptions"/> default.</summary>
        public TimeSpan? PasswordMaxAge { get; private set; }
        public Serilog.ILogger Log { get; private set; }

        public static StationConfig Load()
        {
            var log = LogBootstrap.CreateDefault("logs\\plc.log", Serilog.Events.LogEventLevel.Information);
            var cfg = new StationConfig { Log = log };
            log.Information("{App} starting", AppVersion.DisplayName);

            // GlobalConfig reads config.xml from the working directory, as in RailHMI
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            GlobalConfig.LoadConfiguration(log);
            cfg.IpAddress = GlobalConfig.IpAddress;

            try
            {
                if (File.Exists("config.xml"))
                {
                    var root = XDocument.Load("config.xml").Root;
                    cfg.UsePlc = string.Equals((string)root?.Element("Backend"), "Plc", StringComparison.OrdinalIgnoreCase);
                    int v;
                    if (int.TryParse((string)root?.Element("Rack"), out v)) cfg.Rack = v;
                    if (int.TryParse((string)root?.Element("Slot"), out v)) cfg.Slot = v;
                    if (int.TryParse((string)root?.Element("AutoLogoutMinutes"), out v)) cfg.AutoLogout = TimeSpan.FromMinutes(Math.Max(0, v));
                    if (int.TryParse((string)root?.Element("PasswordMaxAgeDays"), out v)) cfg.PasswordMaxAge = TimeSpan.FromDays(Math.Max(0, v));
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "config.xml could not be read; running the simulation");
                cfg.UsePlc = false;
            }

            if (cfg.UsePlc && string.IsNullOrWhiteSpace(cfg.IpAddress))
            {
                log.Error("config.xml selects the PLC backend but has no IpAddress; running the simulation");
                cfg.UsePlc = false;
            }
            log.Information("Backend: {Backend} {Ip} rack {Rack} slot {Slot}", cfg.UsePlc ? "PLC" : "Simulation", cfg.IpAddress, cfg.Rack, cfg.Slot);
            return cfg;
        }
    }
}
