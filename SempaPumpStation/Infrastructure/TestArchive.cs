using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>One sample of a test, with the values derived from it for the report.</summary>
    public sealed class TestSample
    {
        public int No { get; set; }
        public string Timestamp { get; set; }
        public int PowerSource { get; set; }
        public int Flowmeter { get; set; }
        public int DischargeTransmitter { get; set; }
        public int FlowmeterDevice { get; set; }
        public int DischargeTransmitterDevice { get; set; }
        public int VacuummeterDevice { get; set; }
        public double Flow { get; set; }
        public double DischargePressure { get; set; }
        public double SuctionPressure { get; set; }
        public double WaterTemperature { get; set; }
        public double AmbientTemperature { get; set; }
        public double Vibration { get; set; }
        public double Temperature { get; set; }
        public double Sound { get; set; }
        public double Speed { get; set; }
        public double Voltage { get; set; }
        public double Current { get; set; }
        public double SpeedReference { get; set; }
        public bool PumpRunning { get; set; }

        // Derived
        public double Head { get; set; }            // total head, m
        public double HydraulicPower { get; set; }  // kW

        public string TimeText => Timestamp != null && Timestamp.Length >= 19 ? Timestamp.Substring(11) : Timestamp;
        public string SourceText => Station.Source(PowerSource)?.Name ?? "—";
    }

    /// <summary>Report header values; a missing key reads as empty so XAML can bind <c>Header[KEY]</c> safely.</summary>
    public sealed class HeaderMap : IEnumerable<KeyValuePair<string, string>>
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string this[string key]
        {
            get { string v; return _values.TryGetValue(key, out v) ? v : ""; }
            set { _values[key] = value; }
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A finished test as the HMI archived it.</summary>
    public sealed class TestRecord
    {
        public int TestNo { get; set; }
        public int Zone { get; set; }
        public string Started { get; set; }
        public string Ended { get; set; }
        public string File { get; set; }
        public HeaderMap Header { get; } = new HeaderMap();
        public List<TestSample> Samples { get; } = new List<TestSample>();

        public string Get(string key) => Header[key];
    }

    /// <summary>
    /// Archives a zone's samples when its test ends: the HMI watches StationSamples.Zone[n].TestActive
    /// and, on its falling edge, reads the buffer together with the report master data and writes a
    /// semicolon-separated CSV (Excel, Turkish locale) under %LOCALAPPDATA%\SempaPumpStation\Tests.
    /// </summary>
    public sealed class TestArchive
    {
        // Report master data copied into each test (recipe selections at the time the test ended)
        public static readonly string[] HeaderKeys =
        {
            "WRT_FIRMA_ADI", "WRT_ADRES", "WRT_TESTI_YAPAN",
            "WRT_POMPA_TIPI", "WRT_PMP_SERI_NO", "WRT_PMP_EMME_CAP", "WRT_PMP_BAS_CAP", "WRT_PMP_MIL_MALZ", "WRT_PMP_MONTAJ_TIP",
            "WRT_CARK_MALZ", "WRT_CARK_CAPI", "WRT_SALMAST_TIPI", "WRT_SALYANGOZ_MALZ",
            "WRT_MOTOR_MARKA", "WRT_MOTOR_SERI_NO", "WRT_MOTOR_FRAME", "WRT_MOTOR_HP", "WRT_MOTOR_TIPI", "WRT_MOTOR_VERIM_SIN",
            "WRT_MOTOR_GUCU", "WRT_MOTOR_DEVRI", "WRT_MOTOR_AKIMI", "WRT_MOTOR_COSPHI",
            "GEO_YUKSEKLIK", "DALGIC_PMP_SEL",
        };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly HmiContext _ctx;
        private readonly bool[] _wasActive = new bool[Station.ZoneCount + 1];

        public ObservableCollection<TestRecord> Records { get; } = new ObservableCollection<TestRecord>();

        public event Action<TestRecord> Archived;

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SempaPumpStation", "Tests");

        public TestArchive(HmiContext ctx)
        {
            _ctx = ctx;
            LoadExisting();
        }

        /// <summary>Called once per HMI cycle.</summary>
        public void Poll()
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                bool active = _ctx.GetBool(Station.Samples(z) + "TestActive");
                if (_wasActive[z] && !active) Archive(z);
                _wasActive[z] = active;
            }
        }

        private void Archive(int z)
        {
            string s = Station.Samples(z);
            int testNo = _ctx.GetInt("GLOBAL_DENEY_NO");
            _ctx.SetValue("GLOBAL_DENEY_NO", testNo + 1);

            var r = new TestRecord
            {
                TestNo = testNo,
                Zone = z,
                Started = _ctx.GetDtl(s + "TestStarted"),
                Ended = _ctx.GetDtl(s + "TestEnded"),
            };
            foreach (var k in HeaderKeys) r.Header[k] = Convert.ToString(_ctx.Tag(k)?.TagValue, Inv) ?? "";

            int count = Math.Min(Station.SampleCapacity, _ctx.GetInt(s + "Count"));
            for (int i = 1; i <= count; i++) r.Samples.Add(ReadSample(z, i));
            Derive(r);

            // Instruments used, for the report header
            var last = r.Samples.LastOrDefault();
            if (last != null)
            {
                r.Header["DEBIMETRE"] = Describe(Station.Flowmeter, last.FlowmeterDevice);
                r.Header["BASINC_TR"] = Describe(Station.PressureTransmitter, last.DischargeTransmitterDevice);
                r.Header["VAKUMMETRE"] = Describe(Station.Vacuummeter, last.VacuummeterDevice);
            }

            Directory.CreateDirectory(Folder);
            r.File = Path.Combine(Folder, string.Format("Deney_{0}_Bolge{1}_{2:yyyyMMdd_HHmmss}.csv", testNo, z, DateTime.Now));
            Write(r);
            Records.Insert(0, r);
            Archived?.Invoke(r);
        }

        private string Describe(int category, int device) =>
            device > 0 ? device + " · " + _ctx.GetString(Station.LibraryEntry(category, device) + "Description") : "—";

        private TestSample ReadSample(int z, int i)
        {
            string p = Station.Sample(z, i);
            return new TestSample
            {
                No = i,
                Timestamp = _ctx.GetDtl(p + "Timestamp"),
                PowerSource = _ctx.GetInt(p + "PowerSource"),
                Flowmeter = _ctx.GetInt(p + "Flowmeter"),
                DischargeTransmitter = _ctx.GetInt(p + "DischargeTransmitter"),
                FlowmeterDevice = _ctx.GetInt(p + "FlowmeterDevice"),
                DischargeTransmitterDevice = _ctx.GetInt(p + "DischargeTransmitterDevice"),
                VacuummeterDevice = _ctx.GetInt(p + "VacuummeterDevice"),
                Flow = _ctx.GetDouble(p + "Flow"),
                DischargePressure = _ctx.GetDouble(p + "DischargePressure"),
                SuctionPressure = _ctx.GetDouble(p + "SuctionPressure"),
                WaterTemperature = _ctx.GetDouble(p + "WaterTemperature"),
                AmbientTemperature = _ctx.GetDouble(p + "AmbientTemperature"),
                Vibration = _ctx.GetDouble(p + "Vibration"),
                Temperature = _ctx.GetDouble(p + "Temperature"),
                Sound = _ctx.GetDouble(p + "Sound"),
                Speed = _ctx.GetDouble(p + "Speed"),
                Voltage = _ctx.GetDouble(p + "Voltage"),
                Current = _ctx.GetDouble(p + "Current"),
                SpeedReference = _ctx.GetDouble(p + "SpeedReference"),
                PumpRunning = _ctx.GetBool(p + "PumpRunning"),
            };
        }

        /// <summary>Total head from the pressure difference plus the gauge height difference; a submersible pump ignores suction.</summary>
        public static void Derive(TestRecord r)
        {
            double geo = ParseDouble(r.Get("GEO_YUKSEKLIK"));
            bool submersible = string.Equals(r.Get("DALGIC_PMP_SEL"), "True", StringComparison.OrdinalIgnoreCase);
            foreach (var s in r.Samples)
            {
                double dp = s.DischargePressure - (submersible ? 0.0 : s.SuctionPressure);
                s.Head = dp * 10.197 + geo;
                s.HydraulicPower = s.Flow * s.Head / 367.2;
            }
        }

        // ---------------------------------------------------------------- CSV
        private static readonly string[] Columns =
        {
            "No", "Zaman", "Kaynak", "Debimetre", "BasincTr", "DebimetreCihaz", "BasincTrCihaz", "VakumCihaz",
            "Debi_m3h", "BasmaBasinci_bar", "EmmeBasinci_bar", "Hm_m", "HidrolikGuc_kW",
            "SuSicakligi_C", "OrtamSicakligi_C", "Titresim_mms", "MotorSicakligi_C", "Ses_dB", "Devir_dk",
            "Gerilim_V", "Akim_A", "HizRef_yuzde", "PompaCalisiyor",
        };

        private static void Write(TestRecord r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#DENEY_NO;" + r.TestNo);
            sb.AppendLine("#BOLGE;" + r.Zone);
            sb.AppendLine("#BASLANGIC;" + r.Started);
            sb.AppendLine("#BITIS;" + r.Ended);
            foreach (var kv in r.Header) sb.AppendLine("#" + kv.Key + ";" + Clean(kv.Value));
            sb.AppendLine(string.Join(";", Columns));
            foreach (var s in r.Samples)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    s.No.ToString(Inv), s.Timestamp, s.PowerSource.ToString(Inv), s.Flowmeter.ToString(Inv), s.DischargeTransmitter.ToString(Inv),
                    s.FlowmeterDevice.ToString(Inv), s.DischargeTransmitterDevice.ToString(Inv), s.VacuummeterDevice.ToString(Inv),
                    N(s.Flow), N(s.DischargePressure, "0.000"), N(s.SuctionPressure, "0.000"), N(s.Head), N(s.HydraulicPower),
                    N(s.WaterTemperature), N(s.AmbientTemperature), N(s.Vibration), N(s.Temperature), N(s.Sound), N(s.Speed, "0"),
                    N(s.Voltage), N(s.Current), N(s.SpeedReference), s.PumpRunning ? "1" : "0",
                }));
            }
            File.WriteAllText(r.File, sb.ToString(), new UTF8Encoding(true));
        }

        // Comma decimal separator so Excel in the Turkish locale reads numbers as numbers
        private static string N(double v, string fmt = "0.00") => v.ToString(fmt, Inv).Replace('.', ',');
        private static string Clean(string v) => (v ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

        private void LoadExisting()
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (var f in Directory.GetFiles(Folder, "Deney_*.csv").OrderByDescending(File.GetLastWriteTime).Take(200))
                {
                    var r = Read(f);
                    if (r != null) Records.Add(r);
                }
            }
            catch { /* a broken archive must not stop the HMI */ }
        }

        private static TestRecord Read(string file)
        {
            try
            {
                var r = new TestRecord { File = file };
                bool header = false;
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (line.StartsWith("#"))
                    {
                        int i = line.IndexOf(';');
                        if (i < 0) continue;
                        string k = line.Substring(1, i - 1), v = line.Substring(i + 1);
                        if (k == "DENEY_NO") r.TestNo = (int)ParseDouble(v);
                        else if (k == "BOLGE") r.Zone = (int)ParseDouble(v);
                        else if (k == "BASLANGIC") r.Started = v;
                        else if (k == "BITIS") r.Ended = v;
                        else r.Header[k] = v;
                        continue;
                    }
                    if (!header) { header = true; continue; }

                    var c = line.Split(';');
                    if (c.Length < Columns.Length) continue;
                    r.Samples.Add(new TestSample
                    {
                        No = (int)ParseDouble(c[0]), Timestamp = c[1], PowerSource = (int)ParseDouble(c[2]),
                        Flowmeter = (int)ParseDouble(c[3]), DischargeTransmitter = (int)ParseDouble(c[4]),
                        FlowmeterDevice = (int)ParseDouble(c[5]), DischargeTransmitterDevice = (int)ParseDouble(c[6]),
                        VacuummeterDevice = (int)ParseDouble(c[7]),
                        Flow = ParseDouble(c[8]), DischargePressure = ParseDouble(c[9]), SuctionPressure = ParseDouble(c[10]),
                        Head = ParseDouble(c[11]), HydraulicPower = ParseDouble(c[12]),
                        WaterTemperature = ParseDouble(c[13]), AmbientTemperature = ParseDouble(c[14]), Vibration = ParseDouble(c[15]),
                        Temperature = ParseDouble(c[16]), Sound = ParseDouble(c[17]), Speed = ParseDouble(c[18]),
                        Voltage = ParseDouble(c[19]), Current = ParseDouble(c[20]), SpeedReference = ParseDouble(c[21]),
                        PumpRunning = c[22] == "1",
                    });
                }
                return r;
            }
            catch { return null; }
        }

        private static double ParseDouble(string s)
        {
            double d;
            return double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Any, Inv, out d) ? d : 0.0;
        }
    }
}
