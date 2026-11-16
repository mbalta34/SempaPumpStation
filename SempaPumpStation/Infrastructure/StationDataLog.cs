using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TerakkiFramework.Logging.Data;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The station's data log: which tags are recorded, how often and when (hard-coded for now), and
    /// the outputs chosen in config.xml (<c>&lt;DataLog&gt;Sqlite,Csv,Xml&lt;/DataLog&gt;</c>). Per zone:
    /// <list type="bullet">
    /// <item>hydraulics and drive every second while the zone's test is active;</item>
    /// <item>source, test state and temperatures every 10 seconds, always;</item>
    /// <item>a snapshot each time the operator takes a sample.</item>
    /// </list>
    /// </summary>
    public static class StationDataLog
    {
        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SempaPumpStation", "DataLog");

        private enum When { Always, WhileTest, OnSample }

        private sealed class Entry
        {
            public string Name, Tag;
            public int Zone, CycleMs;
            public When When;
        }

        private static IEnumerable<Entry> Entries()
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string ph = Station.ZonePh(z), n = "Zone" + z + ".";
                Func<string, string> ai = ch => Station.AiPh("Zone" + z + "_" + ch) + "Pv";

                // Hydraulics and drive: every second during a test
                yield return E(z, n + "Flow", ph + "Flow", 1000, When.WhileTest);
                yield return E(z, n + "DischargePressure", ph + "DischargePressure", 1000, When.WhileTest);
                yield return E(z, n + "SuctionPressure", ph + "SuctionPressure", 1000, When.WhileTest);
                yield return E(z, n + "PumpRunning", ph + "PumpRunning", 1000, When.WhileTest);
                yield return E(z, n + "SpeedReference", ph + "SpeedReference.Value", 1000, When.WhileTest);
                yield return E(z, n + "Speed", ai("Speed"), 1000, When.WhileTest);
                yield return E(z, n + "Vibration", ai("Vibration"), 1000, When.WhileTest);
                yield return E(z, n + "Sound", ai("Sound"), 1000, When.WhileTest);

                // Slow values: every 10 seconds, always
                yield return E(z, n + "PowerSource", ph + "PowerSource.Value", 10000, When.Always);
                yield return E(z, n + "TestActive", ph + "TestActive", 10000, When.Always);
                yield return E(z, n + "WaterTemperature", ai("WaterTemperature"), 10000, When.Always);
                yield return E(z, n + "AmbientTemperature", ai("AmbientTemperature"), 10000, When.Always);
                yield return E(z, n + "MotorTemperature", ai("Temperature"), 10000, When.Always);

                // The moment the operator takes a sample
                yield return E(z, n + "Sample.No", ph + "SampleCount", 0, When.OnSample);
                yield return E(z, n + "Sample.Flow", ph + "Flow", 0, When.OnSample);
                yield return E(z, n + "Sample.DischargePressure", ph + "DischargePressure", 0, When.OnSample);
                yield return E(z, n + "Sample.SuctionPressure", ph + "SuctionPressure", 0, When.OnSample);
                yield return E(z, n + "Sample.Speed", ai("Speed"), 0, When.OnSample);
            }
        }

        private static Entry E(int zone, string name, string tag, int cycleMs, When when) =>
            new Entry { Zone = zone, Name = name, Tag = tag, CycleMs = cycleMs, When = when };

        /// <summary>The started logger, or null when config.xml turns logging off.</summary>
        public static DataLogger Start(HmiContext ctx, StationConfig config)
        {
            var sinks = new List<IDataLogSink>();
            foreach (var output in config.DataLogOutputs)
            {
                switch (output)
                {
                    case "sqlite": sinks.Add(new SqliteDataLogSink(Path.Combine(Folder, "DataLog.db"), config.DataLogRetentionDays)); break;
                    // The station's Excel uses the PC's culture: ';' columns, decimal comma on a Turkish PC
                    case "csv": sinks.Add(new CsvDataLogSink(Folder, "DataLog", config.DataLogRetentionDays, ';', CultureInfo.CurrentCulture)); break;
                    case "xml": sinks.Add(new XmlDataLogSink(Folder, "DataLog", config.DataLogRetentionDays)); break;
                    default: config.Log.Warning("config.xml DataLog: unknown output {Output}", output); break;
                }
            }
            if (sinks.Count == 0) return null;

            // One trigger per zone and kind, shared by its tags
            var whileTest = new Dictionary<int, DataLogTrigger>();
            var onSample = new Dictionary<int, DataLogTrigger>();
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var testActive = ctx.Tag(Station.ZonePh(z) + "TestActive");
                var sampleCount = ctx.Tag(Station.ZonePh(z) + "SampleCount");
                if (testActive != null) whileTest[z] = DataLogTrigger.While(testActive, "Zone" + z + "Test");
                if (sampleCount != null) onSample[z] = DataLogTrigger.OnChange(sampleCount, "Zone" + z + "Sample");
            }

            var logger = new DataLogger(sinks, config.Log);
            foreach (var e in Entries())
            {
                var tag = ctx.Tag(e.Tag);
                if (tag == null)
                {
                    config.Log.Warning("Data log: tag {Tag} not in the catalogue", e.Tag);
                    continue;
                }

                DataLogTrigger trigger;
                switch (e.When)
                {
                    case When.WhileTest:
                        if (whileTest.TryGetValue(e.Zone, out trigger)) logger.Add(e.Name, tag, e.CycleMs, trigger);
                        break;
                    case When.OnSample:
                        if (onSample.TryGetValue(e.Zone, out trigger)) logger.Add(e.Name, tag, trigger);
                        break;
                    default:
                        logger.Add(e.Name, tag, e.CycleMs);
                        break;
                }
            }

            logger.Start();
            config.Log.Information("Data log: {Count} tags to {Outputs} in {Folder}", logger.Tags.Count, string.Join(",", config.DataLogOutputs), Folder);
            return logger;
        }
    }
}
