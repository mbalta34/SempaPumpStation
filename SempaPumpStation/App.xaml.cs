using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SempaPumpStation.Infrastructure;
using SempaPumpStation.Shell;
using TerakkiFramework.Utils;

namespace SempaPumpStation
{
    public partial class App : Application
    {
        private HmiContext _context;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, ex) => { Log(ex.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => { Log(ex.ExceptionObject as Exception); };

            try
            {
                // Let the framework marshal tag change notifications onto this UI thread.
                UiSync.Context = SynchronizationContext.Current;

                // LAYOUTDUMP=1: write every S7 tag's address to plc_layout.txt (to check against TIA) and exit
                if (Environment.GetEnvironmentVariable("LAYOUTDUMP") == "1")
                {
                    var link = new StationPlcLink("0.0.0.0", 0, 1, TerakkiFramework.Logging.LogBootstrap.CreateDefault());
                    File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plc_layout.txt"),
                        System.Linq.Enumerable.Select(link.Leaves(), t => t.Address.PadRight(26) + t.DataType.ToString().PadRight(15) + t.Name));
                    Shutdown(0);
                    return;
                }

                _context = new HmiContext();
                await _context.InitializeAsync();

                var vm = new ShellViewModel(_context);
                var shell = new ShellWindow { DataContext = vm };
                MainWindow = shell;
                shell.Show();

                if (Environment.GetEnvironmentVariable("SMOKETEST") == "1")
                    Dispatcher.BeginInvoke(new Action(() => RunSmoke(shell, vm)),
                        DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show(ex.ToString(), "Başlatma hatası");
                Shutdown(1);
            }
        }

        private void RunSmoke(ShellWindow shell, ShellViewModel vm)
        {
            var sb = new System.Text.StringBuilder();
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
            Directory.CreateDirectory(dir);

            try { RunScenario(sb); }
            catch (Exception ex) { sb.AppendLine("FAIL scenario : " + ex); }

            var groups = new[] { vm.MainNav, vm.RecipeNav, vm.DiagNav };
            foreach (var g in groups)
                foreach (var entry in g)
                {
                    try
                    {
                        vm.NavigateCommand.Execute(entry);
                        shell.UpdateLayout();
                        Steps(8);
                        shell.UpdateLayout();
                        Capture(shell, Path.Combine(dir, entry.Key + ".png"));
                        sb.AppendLine("OK   " + entry.Key + " — " + entry.Label);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("FAIL " + entry.Key + " : " + ex.GetType().Name + " — " + ex.Message);
                    }
                }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screens_ok.log"), sb.ToString());
            Shutdown(0);
        }

        // ---------------------------------------------------------------- smoke scenario
        // Drives the simulated PLC through the station's main interlocks and records each check.
        private void RunScenario(System.Text.StringBuilder sb)
        {
            var c = _context;
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);
            string Hp(int z) => Station.ZoneHp(z);
            string Ph(int z) => Station.ZonePh(z);
            string Sim(int z) => Station.SimZone(z);

            Steps(20);   // instruments accepted and loaded, springs charging

            // Zone 1 takes the 37 kW VFD; zone 2 must not get the same source
            c.SetValue(Hp(1) + "PowerSource.Value", (short)4);
            Steps(15);
            Check("zone 1 connects source 4", c.GetInt(Ph(1) + "PowerSource.Value") == 4 && c.GetInt(Ph(1) + "PowerState") == Station.StateConnected);
            c.SetValue(Hp(2) + "PowerSource.Value", (short)4);
            Steps(3);
            Check("zone 2 cannot select source 4", c.GetInt(Ph(2) + "PowerSource.Value") == 0 && c.GetBool(Ph(2) + "SelectionRejected"));
            c.SetValue(Hp(2) + "Flowmeter.Value", (short)1);
            Steps(3);
            Check("zone 2 cannot take shared FM1 of zone 1", c.GetInt(Ph(2) + "Flowmeter.Value") == 2);

            // Sampling needs a test; the pump does not
            Press(Sim(1) + "PendantTakeSample");
            Check("no sample without a test", c.GetInt(Ph(1) + "SampleCount") == 0);
            Press(Sim(1) + "PendantStart");
            Steps(40);
            Check("pump runs without a test", c.GetBool(Ph(1) + "PumpRunning"));
            Check("selection locked while running", !c.GetBool(Ph(1) + "PowerSource.Enabled"));

            c.Click(Hp(1) + "StartTest");
            Steps(3);
            Check("start test", c.GetBool(Ph(1) + "TestActive") && !c.GetBool(Ph(1) + "StartTest.Enabled"));
            foreach (var valve in new[] { 100.0, 80.0, 60.0, 40.0, 25.0, 10.0 })
            {
                c.SetValue(Sim(1) + "ValveOpening", (float)valve);
                Steps(50);
                Press(Sim(1) + "PendantTakeSample");
            }
            Check("six samples taken", c.GetInt(Ph(1) + "SampleCount") == 6);
            double q1 = c.GetDouble(Station.Sample(1, 1) + "Flow"), q6 = c.GetDouble(Station.Sample(1, 6) + "Flow");
            double p1 = c.GetDouble(Station.Sample(1, 1) + "DischargePressure"), p6 = c.GetDouble(Station.Sample(1, 6) + "DischargePressure");
            Check(string.Format("throttling lowers flow and raises pressure (Q {0:0.0}→{1:0.0}, Pd {2:0.00}→{3:0.00})", q1, q6, p1, p6), q6 < q1 && p6 > p1);
            Check("Pt2 scaled with its loaded 0-16 bar range", c.GetDouble(Station.AiPh("Zone1_Pt2") + "MaxPv.Value") == 16.0 && p6 < 6.0);

            int records = c.Archive.Records.Count;
            c.Click(Hp(1) + "EndTest");
            Steps(3);
            Check("end test archives the samples", c.Archive.Records.Count == records + 1 && c.Archive.Records[0].Samples.Count == 6);
            Check("samples stay after end test", c.GetInt(Ph(1) + "SampleCount") == 6);

            // A position cannot take a device mounted elsewhere
            c.SetValue(Station.PositionHp("Zone3_Pt1") + "Device.Value", (short)1);
            Steps(3);
            Check("device mounted twice is rejected", c.GetInt(Station.PositionPh("Zone3_Pt1") + "Device.Value") == 9
                                                     && c.GetBool(Station.PositionPh("Zone3_Pt1") + "SelectionRejected"));
            Check("position locked while its zone runs", !c.GetBool(Station.PositionPh("Zone1_Pt1") + "Device.Enabled"));

            // Zone 3: 560 kW magnetic switch with spring, then E-stop
            c.SetValue(Hp(3) + "PowerSource.Value", (short)7);
            Steps(60);
            Check("zone 3 magnetic switch closes after spring charge", c.GetInt(Ph(3) + "PowerState") == Station.StateConnected);
            c.SetValue(Sim(3) + "EStopPressed", true);
            Steps(5);
            Check("E-stop latches and opens the switch", c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") != Station.StateConnected);
            c.Click(Hp(3) + "ResetFault");
            Steps(3);
            Check("reset ignored while E-stop pressed", c.GetBool(Ph(3) + "EStop"));
            c.SetValue(Sim(3) + "EStopPressed", false);
            c.Click(Hp(3) + "ResetFault");
            Steps(60);
            Check("released + reset clears E-stop and reconnects", !c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") == Station.StateConnected);

            // Zone 1 drive fault stops the pump
            c.SetValue(Station.SimDrive(4) + "Fault", true);
            Steps(3);
            Check("drive fault stops the pump", c.GetBool(Ph(1) + "DriveFault") && !c.GetBool(Station.SimDrive(4) + "Running"));
            c.SetValue(Station.SimDrive(4) + "Fault", false);
            Press(Sim(1) + "PendantStart");
            Steps(40);

            // Zone 2 runs too, for the overview captures
            c.SetValue(Hp(2) + "PowerSource.Value", (short)6);
            Steps(15);
            c.Click(Hp(2) + "StartTest");
            Press(Sim(2) + "PendantStart");
            Steps(40);
            Press(Sim(2) + "PendantTakeSample");
            Steps(10);
        }

        private void Steps(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _context.Step();
                DoEvents();
            }
        }

        // Tag notifications and DataGrid column sizing are posted to the dispatcher; let them run
        private void DoEvents() => Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

        // A pendant press: the momentary tag would drop after 200 ms of real time, so release it explicitly
        private void Press(string tag)
        {
            _context.SetValue(tag, true);
            Steps(2);
            _context.SetFromDevice(tag, false);
            Steps(2);
        }

        private static void Capture(Window shell, string path)
        {
            var root = shell.Content as FrameworkElement;
            if (root == null) return;
            int w = (int)root.ActualWidth, h = (int)root.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        private static void Log(Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log"),
                    (ex?.ToString() ?? "unknown") + Environment.NewLine);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _context?.Shutdown();
            base.OnExit(e);
        }
    }
}
