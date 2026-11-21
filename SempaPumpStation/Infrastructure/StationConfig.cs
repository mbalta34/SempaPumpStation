using System;
using System.IO;
using System.Linq;
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
    /// &lt;DataLog&gt;Sqlite&lt;/DataLog&gt;                     data log outputs: any of Sqlite, Csv, Xml (comma-separated), None; default Sqlite
    /// &lt;DataLogRetentionDays&gt;90&lt;/DataLogRetentionDays&gt;  data older than this is deleted, 0 = keep everything
    /// &lt;ReconnectAttempts&gt;3&lt;/ReconnectAttempts&gt;          a lost PLC link is tried again this many times per round
    /// &lt;ReconnectIntervalSeconds&gt;30&lt;/ReconnectIntervalSeconds&gt;  a new round this often while the PLC is unreachable
    /// </code>
    /// </summary>
    public sealed class StationConfig
    {
        /// <summary>Lower-case output names: sqlite, csv, xml. Empty = no data log.</summary>
        public string[] DataLogOutputs { get; private set; } = { "sqlite" };
        public int DataLogRetentionDays { get; private set; } = 90;
        public bool UsePlc { get; private set; }
        public string IpAddress { get; private set; }
        public int Rack { get; private set; }
        public int Slot { get; private set; } = 1;
        public int ReconnectAttempts { get; private set; } = 3;
        public int ReconnectIntervalSeconds { get; private set; } = 30;
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
                    if (int.TryParse((string)root?.Element("DataLogRetentionDays"), out v)) cfg.DataLogRetentionDays = Math.Max(0, v);
                    if (int.TryParse((string)root?.Element("ReconnectAttempts"), out v)) cfg.ReconnectAttempts = Math.Max(1, v);
                    if (int.TryParse((string)root?.Element("ReconnectIntervalSeconds"), out v)) cfg.ReconnectIntervalSeconds = Math.Max(5, v);
                    var outputs = (string)root?.Element("DataLog");
                    if (outputs != null)
                        cfg.DataLogOutputs = outputs.ToLowerInvariant()
                            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(o => o != "none").Distinct().ToArray();
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
