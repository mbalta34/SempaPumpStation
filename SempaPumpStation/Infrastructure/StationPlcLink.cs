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
        private readonly List<S7DbReadPlan> _plans = new List<S7DbReadPlan>();

        // Sharp7 results meaning the PLC's data blocks do not have the layout of this HMI's interface
        private const int ErrAddressOutOfRange = 0x00900000, ErrItemNotAvailable = 0x00C00000;
        private CancellationTokenSource _cts;

        public StationHpDb Hp { get; }
        public StationPhDb Ph { get; }
        public StationSamplesDb Samples { get; }
        public StationInstrumentsDb Library { get; }
        public MaintenancePhDb Maintenance { get; }
        /// <summary>HMI-to-PLC side of the IO channels (forcing); written only, like StationHp.</summary>
        public MaintenanceHpDb MaintenanceHp { get; }
        /// <summary>Raw values of the IO-Link hubs, for the IO-Link screen.</summary>
        public IoLinkDb IoLink { get; }

        public string IpAddress { get; }
        public bool IsConnected => _connection.State == ConnectionState.Connected;
        public IConnectionService Connection => _connection;

        /// <summary>Why the link was stopped (the PLC program does not match this HMI), or null.</summary>
        public string Problem { get; private set; }

        /// <summary>Connected, and every block is being read.</summary>
        public bool DataOk => IsConnected && Problem == null && _plans.All(p => p.ConsecutiveFailures == 0);

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
            MaintenanceHp = new MaintenanceHpDb(_connection, w, ReadPlan.Individual);
            IoLink = new IoLinkDb(_connection, w, ReadPlan.DbBulk);

            // Status and live values fast; the sample buffers once a second (12 kB, consistent with
            // TestActive since both come from one read); the library rarely changes.
            var ai = Maintenance.Io.AnalogInputs;
            var din = Maintenance.Io.DigitalInputs;
            var dout = Maintenance.Io.DigitalOutputs;
            AddPlan("PH", 150, StationPhDb.Number, 0, S7Layout.SizeOf(Ph), Ph);
            AddPlan("AI", 300, MaintenancePhDb.Number, ai.ByteOffset, S7Layout.SizeOf(ai), ai);
            AddPlan("DI", 300, MaintenancePhDb.Number, din.ByteOffset, S7Layout.SizeOf(din), din);
            AddPlan("DO", 300, MaintenancePhDb.Number, dout.ByteOffset, S7Layout.SizeOf(dout), dout);
            AddPlan("SAMPLES", 1000, StationSamplesDb.Number, 0, S7Layout.SizeOf(Samples), Samples);
            AddPlan("LIBRARY", 5000, StationInstrumentsDb.Number, 0, S7Layout.SizeOf(Library), Library);
            AddPlan("IOLINK", 500, IoLinkDb.Number, 0, S7Layout.SizeOf(IoLink), IoLink);

            // Logged so a drift between these models and the PLC's DBs shows up at once (compare with
            // the DB lengths / offsets in TIA)
            _log.Information("Interface layout: StationHp DB{Hp} {HpLen} B | StationPh DB{Ph} {PhLen} B | StationSamples DB{S} {SLen} B | " +
                             "StationInstruments DB{L} {LLen} B | MaintenancePh DB{M} AnalogInputs @{AiOff} {AiLen} B, DigitalInputs @{DiOff} {DiLen} B, DigitalOutputs @{DoOff} {DoLen} B | " +
                             "MaintenanceHp DB{MH} {MHLen} B | IoLink DB{IoL} {IoLLen} B",
                StationHpDb.Number, S7Layout.SizeOf(Hp), StationPhDb.Number, S7Layout.SizeOf(Ph),
                StationSamplesDb.Number, S7Layout.SizeOf(Samples), StationInstrumentsDb.Number, S7Layout.SizeOf(Library),
                MaintenancePhDb.Number, ai.ByteOffset, S7Layout.SizeOf(ai), din.ByteOffset, S7Layout.SizeOf(din), dout.ByteOffset, S7Layout.SizeOf(dout),
                MaintenanceHpDb.Number, S7Layout.SizeOf(MaintenanceHp), IoLinkDb.Number, S7Layout.SizeOf(IoLink));
        }

        private void AddPlan(string id, int intervalMs, int db, int start, int length, SiemensS7PlcTag tag)
        {
            var plan = new S7DbReadPlan(_log, id, intervalMs, db, start, length);
            _plans.Add(plan);
            _connection.AddPlan(plan);
            _connection.RegisterTagToPlan(id, tag);
        }

        /// <summary>Every leaf tag the HMI binds to: StationHp/Ph, the sample buffers, the library, the analog and digital inputs, the digital outputs (state and forcing) and the IO-Link hubs.</summary>
        public IEnumerable<SiemensS7PlcTag> Leaves() =>
            new SiemensS7PlcTag[] { Hp, Ph, Samples, Library, Maintenance.Io.AnalogInputs, Maintenance.Io.DigitalInputs, Maintenance.Io.DigitalOutputs, MaintenanceHp.Io.DigitalOutputs, IoLink }.SelectMany(Flatten);

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

        /// <summary>
        /// Called every HMI scan. A block that is missing or shorter than the generated interface means the PLC runs
        /// another program version: its offsets cannot be trusted, so the link stops (no more writes) until restart.
        /// </summary>
        public void CheckInterface()
        {
            if (Problem != null) return;
            var bad = _plans.FirstOrDefault(p => p.ConsecutiveFailures > 0
                                                 && (p.LastErrorCode == ErrAddressOutOfRange || p.LastErrorCode == ErrItemNotAvailable));
            if (bad == null) return;

            Problem = (bad.LastErrorCode == ErrItemNotAvailable ? "PLC'de DB" + bad.Db + " yok" : "PLC'deki DB" + bad.Db + " HMI'nın beklediğinden kısa")
                    + ": PLC programı bu HMI sürümüyle uyumsuz. Güncel programı PLC'ye yükleyip HMI'yı yeniden başlatın.";
            _log.Error("Interface mismatch on plan {Plan} (DB{Db}, {Len} B from {Start}): {Err}. Link stopped; nothing is written to the PLC until restart.",
                       bad.Id, bad.Db, bad.Length, bad.Start, bad.LastErrorCode);
            Stop();
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
