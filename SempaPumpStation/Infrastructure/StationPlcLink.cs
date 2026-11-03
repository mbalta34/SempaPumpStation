using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SempaPumpStation.PlcInterface;
using TerakkiFramework.Communication.Abstractions;
using TerakkiFramework.Communication.Config;
using TerakkiFramework.Communication.Drivers.SiemensS7;
using TerakkiFramework.Plc;
using TerakkiFramework.Plc.Bulk.Siemens;
using TerakkiFramework.Plc.Siemens;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The S7 connection to MainPLC, built like RailHMI's: typed tag models over the standard-access
    /// interface DBs, the PLC-to-HMI blocks read in bulk by <see cref="S7DbReadPlan"/>s, the HMI-to-PLC
    /// block (StationHp) written per tag and never read. The leaf tags carry the PLC symbol paths as
    /// names, so the screens bind to them exactly as they do to the simulation's tags.
    /// </summary>
    public sealed class StationPlcLink
    {
        private const int ReconnectDelayMs = 5000;

        private readonly Serilog.ILogger _log;
        private readonly SiemensS7IsoTcpConnection _connection;
        private CancellationTokenSource _cts;

        public StationHpDb Hp { get; }
        public StationPhDb Ph { get; }
        public StationSamplesDb Samples { get; }
        public StationInstrumentsDb Library { get; }
        public MaintenancePhDb Maintenance { get; }

        public string IpAddress { get; }
        public bool IsConnected => _connection.State == ConnectionState.Connected;
        public IConnectionService Connection => _connection;

        public StationPlcLink(string ipAddress, int rack, int slot, Serilog.ILogger log)
        {
            _log = log;
            IpAddress = ipAddress;
            _connection = new SiemensS7IsoTcpConnection(new SiemensS7IsoTcpConfig { IpAddress = ipAddress, Rack = rack, Slot = slot }, log);

            const WriteDispatchMode w = WriteDispatchMode.Immediate;
            Hp = new StationHpDb(_connection, w, ReadPlan.Individual);
            Ph = new StationPhDb(_connection, w, ReadPlan.DbBulk);
            Samples = new StationSamplesDb(_connection, w, ReadPlan.DbBulk);
            Library = new StationInstrumentsDb(_connection, w, ReadPlan.DbBulk);
            Maintenance = new MaintenancePhDb(_connection, w, ReadPlan.DbBulk);

            // Status and live values fast; the sample buffers once a second (12 kB, consistent with
            // TestActive since both come from one read); the library rarely changes.
            var ai = Maintenance.Io.AnalogInputs;
            AddPlan("PH", 150, StationPhDb.Number, 0, S7Layout.SizeOf(Ph), Ph);
            AddPlan("AI", 300, MaintenancePhDb.Number, ai.ByteOffset, S7Layout.SizeOf(ai), ai);
            AddPlan("SAMPLES", 1000, StationSamplesDb.Number, 0, S7Layout.SizeOf(Samples), Samples);
            AddPlan("LIBRARY", 5000, StationInstrumentsDb.Number, 0, S7Layout.SizeOf(Library), Library);

            // Logged so a drift between these models and the PLC's DBs shows up at once (compare with
            // the DB lengths / offsets in TIA)
            _log.Information("Interface layout: StationHp DB{Hp} {HpLen} B | StationPh DB{Ph} {PhLen} B | StationSamples DB{S} {SLen} B | " +
                             "StationInstruments DB{L} {LLen} B | MaintenancePh DB{M} AnalogInputs @{AiOff} {AiLen} B",
                StationHpDb.Number, S7Layout.SizeOf(Hp), StationPhDb.Number, S7Layout.SizeOf(Ph),
                StationSamplesDb.Number, S7Layout.SizeOf(Samples), StationInstrumentsDb.Number, S7Layout.SizeOf(Library),
                MaintenancePhDb.Number, ai.ByteOffset, S7Layout.SizeOf(ai));
        }

        private void AddPlan(string id, int intervalMs, int db, int start, int length, SiemensS7PlcTag tag)
        {
            _connection.AddPlan(new S7DbReadPlan(_log, id, intervalMs, db, start, length));
            _connection.RegisterTagToPlan(id, tag);
        }

        /// <summary>Every leaf tag the HMI binds to: StationHp/Ph, the sample buffers, the library and the analog inputs.</summary>
        public IEnumerable<SiemensS7PlcTag> Leaves() =>
            new SiemensS7PlcTag[] { Hp, Ph, Samples, Library, Maintenance.Io.AnalogInputs }.SelectMany(Flatten);

        private static IEnumerable<SiemensS7PlcTag> Flatten(SiemensS7PlcTag tag)
        {
            if (tag.DataType != DataType.STRUCT && tag.DataType != DataType.ARRAY)
            {
                yield return tag;
                yield break;
            }
            foreach (var p in tag.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                if (p.DeclaringType == typeof(SiemensS7PlcTag)) continue;
                var v = p.GetValue(tag);
                if (v is SiemensS7PlcTag child)
                    foreach (var leaf in Flatten(child)) yield return leaf;
                else if (v is IEnumerable list && !(v is string))
                    foreach (var item in list.OfType<SiemensS7PlcTag>())
                        foreach (var leaf in Flatten(item)) yield return leaf;
            }
        }

        /// <summary>Connects, and reconnects every few seconds while the PLC is unreachable.</summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (_connection.State == ConnectionState.Disconnected)
                    {
                        try { await _connection.Connect(); }
                        catch (Exception ex) { _log.Warning("PLC {Ip} not reachable: {Msg}", IpAddress, ex.Message); }
                    }
                    try { await Task.Delay(ReconnectDelayMs, token); } catch (OperationCanceledException) { }
                }
            });
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _connection.Disconnect().Wait(2000); } catch { }
        }
    }
}
