using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using SempaPumpStation.Infrastructure.Plc;
using SempaPumpStation.Infrastructure.Security;
using SempaPumpStation.Shell;
using TerakkiFramework.Communication.Abstractions;
using TerakkiFramework.Logging;
using TerakkiFramework.Logging.Audit;
using TerakkiFramework.Logging.Data;
using TerakkiFramework.Plc.Abstractions;
using TerakkiFramework.Plc.Hmi;
using TerakkiFramework.Plc.Local;
using TerakkiFramework.Security;
using TerakkiFramework.Tags;
using TerakkiUI.Components.User;
using TerakkiUI.Dialogs;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The application's live runtime. config.xml chooses the backend:
    /// <list type="bullet">
    /// <item>Simulation (default): every tag is on the in-memory Local backend and a port of the station
    /// program runs every 100 ms together with the field it controls.</item>
    /// <item>Plc: the PLC data blocks are the generated S7 models of <see cref="StationPlcLink"/>; only the
    /// HMI-local tags (recipes, report data) stay on the Local backend.</item>
    /// </list>
    /// Either way the tags are exposed by their PLC symbol path for XAML binding.
    /// </summary>
    public sealed class HmiContext
    {
        public const double ScanMs = 100;

        /// <summary>How long a command button's Click stays TRUE against the real PLC (BluePrism FB_Hmi_CommandButton acts on its rising edge).</summary>
        public const int ClickPulseMs = 300;

        private IConnectionService _connection;
        private TagProjectRuntime _runtime;
        private DispatcherTimer _timer;

        /// <summary>The running context, for controls that write PLC parameters (TagTextBox).</summary>
        public static HmiContext Current { get; private set; }

        // Hp writes that complete an entry: the value, written once the PLC has seen the step before it
        private sealed class PendingWrite
        {
            public IPlcTag Tag;
            public object Value;
            public int ScansLeft;
        }
        private readonly List<PendingWrite> _pending = new List<PendingWrite>();

        /// <summary>Live tags keyed by name — the binding surface for every screen.</summary>
        public Dictionary<string, IPlcTag> Tags { get; } = new Dictionary<string, IPlcTag>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The simulated PLC; null when connected to the real one.</summary>
        public StationPlc Plc { get; private set; }
        /// <summary>The real PLC connection; null in simulation.</summary>
        public StationPlcLink Link { get; private set; }
        public bool IsSimulation => Link == null;
        public string BackendText => IsSimulation ? "Simülasyon" : "PLC " + Link.IpAddress;

        public TestArchive Archive { get; private set; }
        /// <summary>The continuous data log; null when config.xml turns it off.</summary>
        public DataLogger DataLog { get; private set; }
        public UserSecurity Security { get; private set; }
        /// <summary>Idle time after which the user is logged out (config.xml AutoLogoutMinutes); zero = never.</summary>
        public TimeSpan AutoLogout { get; private set; }
        /// <summary>Message / confirmation dialogs in the HMI's theme.</summary>
        public IDialogService Dialogs { get; } = new DialogService(() => new MessageDialogWindow());
        public IConnectionService Connection => _connection;

        /// <summary>Raised after every PLC scan, on the UI thread.</summary>
        public event EventHandler Scanned;

        /// <summary>Set by the shell so any screen can request navigation by key.</summary>
        public Action<string> Navigator { get; set; }
        public void Navigate(string key) => Navigator?.Invoke(key);

        public TagScope Scope(string prefix) => new TagScope(Tags, prefix);

        /// <param name="forceSimulation">Ignore config.xml's backend; the smoke test must never write to a real PLC.</param>
        public async Task InitializeAsync(bool forceSimulation = false)
        {
            var config = StationConfig.Load();
            bool simulate = forceSimulation || !config.UsePlc;

            // The framework login system with the station's levels and Turkish texts
            var options = new SecurityOptions();
            if (config.PasswordMaxAge.HasValue) options.PasswordMaxAge = config.PasswordMaxAge.Value;
            Security = new UserSecurity(StationSecurity.DataFolder, StationSecurity.Table, options, StationSecurity.Turkish());
            AutoLogout = config.AutoLogout;
            Access.User = Security.User;
            // Operator actions (logins, user changes, framework controls) go to logs\audit.log under the logged-in user
            AuditLog.Instance = new SerilogAuditLogger(LogBootstrap.CreateDefault("logs\\audit.log"), Security.User);

            var project = TagCatalog.Build(simulate);
            IPlcTagFactory factory;
            _connection = LocalProjectConnectionFactory.Create(out factory);
            _runtime = new TagProjectRuntime(_connection, factory);

            await _runtime.GoOnlineAsync(project, onTagsCreated: tags =>
            {
                foreach (var tag in tags)
                    Tags[tag.Name] = tag;
            });

            // HMI-local starting state (device-side push, not operator writes)
            foreach (var kv in TagCatalog.InitialValues()) SetFromDevice(kv.Key, kv.Value);

            if (simulate)
            {
                // The retained PLC library, as a commissioned station would have it
                foreach (var kv in InstrumentLibrarySeed.LibraryValues()) SetFromDevice(kv.Key, kv.Value);
                SetFromDevice(Station.NextTestNo, 1042);   // the retained test counter
                Plc = new StationPlc(new PlcTags(Tags), InstrumentLibrarySeed.MountedDevice);
            }
            else
            {
                // Before the link builds its tags: Click is pulsed, the PLC never clears it
                st_Hmi_Hp_CommandButton.ClickPulseMs = ClickPulseMs;
                Link = new StationPlcLink(config.IpAddress, config.Rack, config.Slot, config.Log, config.ReconnectAttempts, config.ReconnectIntervalSeconds);
                foreach (var leaf in Link.Leaves()) Tags[leaf.Name] = leaf;
                SetFromDevice("PLC_COMM_OK", false);
                Link.Start();
            }

            Archive = new TestArchive(this);
            DataLog = StationDataLog.Start(this, config);

            Current = this;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ScanMs) };
            _timer.Tick += (s, e) => Step();
            _timer.Start();
        }

        /// <summary>The HMI's own scan timer; the smoke test pauses it where a check counts scans exactly.</summary>
        public bool ScanTimerEnabled
        {
            get => _timer != null && _timer.IsEnabled;
            set { if (_timer != null) _timer.IsEnabled = value; }
        }

        /// <summary>One PLC scan plus the HMI's own cyclic work. Public so the smoke test can fast-forward.</summary>
        public void Step()
        {
            CompletePendingWrites();   // entries and click releases due after the scan before this one
            if (Plc != null) Plc.Scan(ScanMs);
            else
            {
                // Connected and every block read; a program that does not match this HMI stops the link
                Link.CheckInterface();
                SetFromDevice("PLC_COMM_OK", Link.DataOk);
                SetFromDevice("PLC_INTERFACE_OK", Link.Problem == null);
                SetFromDevice("PLC_LINK_TEXT", Link.StatusText);
            }
            UpdatePlcVersion();
            SetFromDevice("TARIH_SAAT", DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            Archive.Poll();
            Scanned?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The program version the PLC reports ("StationPh".PlcVersion), empty until it has been read.</summary>
        public string PlcVersion => (GetString("StationPh.PlcVersion") ?? string.Empty).Trim();

        /// <summary>
        /// "PLC 1.3.4" for the rail, and PLC_VERSION_OK for the alarm list: false when the PLC runs another program
        /// version than <see cref="AppVersion.ExpectedPlcVersion"/>. Not yet read is no mismatch (the link alarms cover it).
        /// </summary>
        private void UpdatePlcVersion()
        {
            string version = PlcVersion;
            SetFromDevice("PLC_VERSION_TEXT", "PLC " + (version.Length > 0 ? version : "—"));
            SetFromDevice("PLC_VERSION_OK", version.Length == 0 || version == AppVersion.ExpectedPlcVersion);
        }

        public void Shutdown()
        {
            _timer?.Stop();
            DataLog?.Dispose();
            Link?.Stop();
            _runtime?.Dispose();
        }

        // ---------------- Station helpers that differ per backend ----------------
        /// <summary>The zone that has source s connected, 0 = none.</summary>
        public int FedZone(int source)
        {
            if (Plc != null) return Plc.Field.FedZone(source);
            for (int z = 1; z <= Station.ZoneCount; z++)
                if (GetInt(Station.ZonePh(z) + "PowerSource.Value") == source && GetInt(Station.ZonePh(z) + "PowerState") == Station.StateConnected)
                    return z;
            return 0;
        }

        /// <summary>Whether the drive of source s runs (in the PLC: the pump of the zone it feeds runs).</summary>
        public bool SourceRunning(int source)
        {
            if (Plc != null) return GetBool(Station.SimDrive(source) + "Running");
            int z = FedZone(source);
            return z > 0 && GetBool(Station.ZonePh(z) + "PumpRunning");
        }

        /// <summary>A DTL as "yyyy-MM-dd HH:mm:ss"; empty for the PLC's 1970 / unset value.</summary>
        public string GetDtl(string dtl)
        {
            int year = GetInt(dtl + ".Year");
            if (year < 2000) return string.Empty;
            return string.Format("{0:0000}-{1:00}-{2:00} {3:00}:{4:00}:{5:00}", year, GetInt(dtl + ".Month"), GetInt(dtl + ".Day"),
                                 GetInt(dtl + ".Hour"), GetInt(dtl + ".Minute"), GetInt(dtl + ".Second"));
        }

        // ---------------- Accessors ----------------
        public IPlcTag Tag(string name)
        {
            IPlcTag t;
            return Tags.TryGetValue(name, out t) ? t : null;
        }

        public bool GetBool(string name)
        {
            var v = Tag(name)?.TagValue;
            if (v is bool b) return b;
            try { return v != null && Convert.ToBoolean(v); } catch { return false; }
        }

        public double GetDouble(string name)
        {
            var v = Tag(name)?.TagValue;
            try { return v == null ? 0 : Convert.ToDouble(v); } catch { return 0; }
        }

        /// <summary>
        /// A PLC Real for display and editing: rounded to 4 decimal places, so 0.02 reads 0.02 and not 0.0199999995529652.
        /// </summary>
        public double GetReal(string name) => Math.Round(GetDouble(name), 4);

        public int GetInt(string name) => (int)Math.Round(GetDouble(name));

        public string GetString(string name) => Tag(name)?.TagValue?.ToString() ?? string.Empty;

        // ---------------- Operator writes (HMI → tag) ----------------
        public void SetValue(string name, object value)
        {
            var t = Tag(name);
            if (t != null) t.TagValue = value;
        }

        public void SetBool(string name, bool value) => SetValue(name, value);
        public void Toggle(string name) => SetValue(name, !GetBool(name));

        /// <summary>Click of an HMI command button: a press is counted in the button's EntryCounter, which the PLC acts on.</summary>
        public void Click(string hpButton)
        {
            var click = Tag(hpButton + ".Click");
            if (click == null) return;
            click.TagValue = true;
            // Against the PLC the tag pulses Click (ClickPulseMs) and counts the press (TerakkiFramework entry counter);
            // in the simulation the press is counted here and Click released once the simulated PLC has scanned it.
            if (IsSimulation)
            {
                CountEntry(click);
                Later(click, false, 2);
            }
        }

        // ---------------- HMI parameter entries ----------------
        // FB_Hmi_Parameter* (RailBase 1.9.2): the PLC never writes its HMI→PLC block and acts on a change of the parameter's
        // EntryCounter, not of its value. Every entry is counted (1 … 255, never 0), so re-entering the value the PLC refused
        // or replaced, or the value Hp already holds, is an entry too.

        /// <summary>Operator entry of an HMI parameter: Hp value tag, its Ph value tag (the PLC's value), new value.</summary>
        public void WriteParameter(IPlcTag hpValue, IPlcTag phValue, object value)
        {
            if (hpValue == null) return;
            hpValue.TagValue = value;   // against the PLC the tag writes the value and then counts the entry
            if (IsSimulation) CountEntry(hpValue);
        }

        /// <summary>The simulation's side of the entry counter: the Hp parameter or button the tag belongs to counts one entry.</summary>
        private void CountEntry(IPlcTag hpTag)
        {
            string name = hpTag.Name;
            int dot = name.LastIndexOf('.');
            var counter = dot > 0 ? Tag(name.Substring(0, dot) + ".EntryCounter") : null;
            if (counter == null) return;
            counter.TagValue = (short)HmiEntry.Next(Convert.ToInt32(counter.TagValue ?? 0));
        }

        /// <summary><see cref="WriteParameter(IPlcTag, IPlcTag, object)"/> by tag names.</summary>
        public void WriteParameter(string hpValue, string phValue, object value) => WriteParameter(Tag(hpValue), Tag(phValue), value);

        private void Later(IPlcTag tag, object value, int scans) =>
            _pending.Add(new PendingWrite { Tag = tag, Value = value, ScansLeft = Math.Max(1, scans) });

        /// <summary>Called at the start of every scan: a write queued with N scans lands before scan N, so the PLC has scanned the step before it.</summary>
        private void CompletePendingWrites()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (--p.ScansLeft > 0) continue;
                _pending.RemoveAt(i);
                p.Tag.TagValue = p.Value;
            }
        }

        /// <summary>Momentary field signal (a Sim.* MOMENTARY_BOOL pulses back to false by itself).</summary>
        public void Pulse(string name) => SetValue(name, true);

        // ---------------- Device push ----------------
        public void SetFromDevice(string name, object value)
        {
            var t = Tag(name) as LocalPlcTag;
            if (t != null) t.SetFromDevice(value);
        }
    }
}
