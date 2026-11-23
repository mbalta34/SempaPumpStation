using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SempaPumpStation.Infrastructure;
using SempaPumpStation.Infrastructure.Security;
using TerakkiUI.Components.User;
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

                // The smoke test logs in, changes passwords and locks accounts: keep that away from the real users.xml
                bool smoke = Environment.GetEnvironmentVariable("SMOKETEST") == "1";
                if (smoke)
                {
                    StationSecurity.DataFolder = Path.Combine(Path.GetTempPath(), "SempaPumpStationSmoke");
                    if (Directory.Exists(StationSecurity.DataFolder)) Directory.Delete(StationSecurity.DataFolder, true);
                }

                // The smoke scenario writes station commands, so it always runs on the simulation
                _context = new HmiContext();
                await _context.InitializeAsync(forceSimulation: smoke);

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
                MessageBox.Show(ex.ToString(), AppVersion.DisplayName + " — başlatma hatası");
                Shutdown(1);
            }
        }

        private void RunSmoke(ShellWindow shell, ShellViewModel vm)
        {
            var sb = new System.Text.StringBuilder();
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
            Directory.CreateDirectory(dir);

            bool coversScreen = shell.IsFullScreen && shell.WindowState == WindowState.Normal
                                && Math.Abs(shell.ActualWidth - SystemParameters.PrimaryScreenWidth) < 1
                                && Math.Abs(shell.ActualHeight - SystemParameters.PrimaryScreenHeight) < 1;
            sb.AppendLine((coversScreen ? "PASS " : "FAIL ") + string.Format("full screen ({0}x{1} on a {2}x{3} screen)",
                shell.ActualWidth, shell.ActualHeight, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));
            shell.SetFullScreen(false);
            DoEvents();
            bool windowed = !shell.IsFullScreen && shell.ActualWidth < SystemParameters.PrimaryScreenWidth;
            shell.SetFullScreen(true);
            DoEvents();
            sb.AppendLine((windowed && shell.IsFullScreen ? "PASS " : "FAIL ") + "▢ switches to a window and back");

            try { RunScenario(sb); }
            catch (Exception ex) { sb.AppendLine("FAIL scenario : " + ex); }
            try { RunIoLinkScenario(sb, vm); }
            catch (Exception ex) { sb.AppendLine("FAIL io-link : " + ex); }
            try { RunOutputForceScenario(sb, vm); }
            catch (Exception ex) { sb.AppendLine("FAIL output-force : " + ex); }
            try { RunReconnectPolicyScenario(sb); }
            catch (Exception ex) { sb.AppendLine("FAIL reconnect : " + ex); }
            try { RunEditScenario(sb); }
            catch (Exception ex) { sb.AppendLine("FAIL edit : " + ex); }
            try { RunSecurityScenario(sb); }   // ends logged in as admin, so the captures show every screen unlocked
            catch (Exception ex) { sb.AppendLine("FAIL security : " + ex); }

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

            // IO screen: the analog inputs and the output force table as well
            var ioEntry = System.Linq.Enumerable.First(vm.DiagNav, e => e.Key == "Io");
            var io = (Screens.IoViewModel)ioEntry.Screen;
            vm.NavigateCommand.Execute(ioEntry);
            foreach (var (tab, file) in new[] { (Screens.IoViewModel.AnalogInputsTab, "Io_Analog"), (Screens.IoViewModel.DigitalOutputsTab, "Io_Outputs"), (Screens.IoViewModel.AnalogOutputsTab, "Io_AnalogOutputs") })
            {
                io.ShowTab(tab);
                shell.UpdateLayout();
                Steps(8);
                Capture(shell, Path.Combine(dir, file + ".png"));
                sb.AppendLine("OK   " + file);
            }
            io.ShowTab(Screens.IoViewModel.DigitalInputsTab);

            // IO-Link screen of zone 3 (stopped, so unlocked) with a transmitter port selected
            var ioLinkEntry = System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "IoLink");
            var ioLink = (Screens.IoLinkViewModel)ioLinkEntry.Screen;
            vm.NavigateCommand.Execute(ioLinkEntry);
            ioLink.Zones[2].Command.Execute(null);
            ioLink.Hubs[1].Ports[1].SelectCommand.Execute(null);
            shell.UpdateLayout();
            Steps(8);
            Capture(shell, Path.Combine(dir, "IoLink_Zone3.png"));
            sb.AppendLine((ioLink.HasSelection && ioLink.CanAssign ? "OK   " : "FAIL ") + "IoLink_Zone3 (port selected, unlocked)");

            // Instruments: the library on its own tab
            var instrumentsEntry = System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "Instruments");
            var instrumentsScreen = (Screens.InstrumentsViewModel)instrumentsEntry.Screen;
            vm.NavigateCommand.Execute(instrumentsEntry);
            instrumentsScreen.ShowTab(Screens.InstrumentsViewModel.LibraryTab);
            shell.UpdateLayout();
            Steps(8);
            Capture(shell, Path.Combine(dir, "Instruments_Library.png"));
            sb.AppendLine((instrumentsScreen.ShowLibrary && !instrumentsScreen.ShowPositions ? "OK   " : "FAIL ") + "Instruments_Library (own tab)");
            instrumentsScreen.ShowTab(Screens.InstrumentsViewModel.PositionsTab);

            // The login modal blocks until closed: capture it from inside, then cancel it
            vm.NavigateCommand.Execute(vm.MainNav[1]);
            bool shown = false;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var login = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<LoginWindow>(Windows));
                if (login == null) return;
                shown = true;
                // Filled in, so the capture shows whether the password dots fit their box
                var lvm = (LoginViewModel)login.DataContext;
                lvm.Username = "operator";
                lvm.Password = "sifre1234";
                login.UpdateLayout();
                Capture(login, Path.Combine(dir, "LoginDialog.png"));
                ((LoginViewModel)login.DataContext).CancelCommand.Execute(null);
            }));
            vm.LoginCommand.Execute(null);
            sb.AppendLine((shown ? "OK   " : "FAIL ") + "LoginDialog");

            // TerakkiUI's default views (this HMI has its own; they are rendered here with its TerakkiUser.* colours)
            var sec = _context.Security;
            CaptureDefault(new LoginView { DataContext = new LoginViewModel(sec, "op1") }, dir, "Default_Login", sb);
            CaptureDefault(new ChangePasswordView { DataContext = new ChangePasswordViewModel(sec, forced: true) }, dir, "Default_ChangePassword", sb);
            CaptureDefault(new UserManagementView { DataContext = new UserManagementViewModel(sec, _context.Dialogs), Width = 1150, Height = 620 },
                           dir, "Default_UserManagement", sb);
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
            bool Lamp(int z, string n) => c.GetBool("MaintenancePh.Io.DigitalOutputs.Zone" + z + "_" + n + ".Pv");

            // PLC mode has only the tags the link creates: every PLC tag the simulation knows must exist there too,
            // or the screens read nothing and their writes are dropped (MaintenanceHp's analog inputs once were missing)
            {
                var link = new StationPlcLink("0.0.0.0", 0, 1, TerakkiFramework.Logging.LogBootstrap.CreateDefault());
                var plcNames = new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Select(link.Leaves(), t => t.Name));
                string[] plcDbs = { "StationHp.", "StationPh.", "StationSamples.", "StationInstruments.", "MaintenanceHp.", "MaintenancePh.", "IoLink.", "StationCounters.", "Powermeters." };
                var missing = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(c.Tags.Keys,
                    n => System.Linq.Enumerable.Any(plcDbs, db => n.StartsWith(db, StringComparison.Ordinal)) && !plcNames.Contains(n)));
                Check("every PLC tag of the screens exists in PLC mode (" + missing.Count + " missing" + (missing.Count > 0 ? ", e.g. " + string.Join(", ", System.Linq.Enumerable.Take(missing, 3)) : "") + ")",
                      missing.Count == 0 && plcNames.Contains(Station.AiHp("Zone1_Speed") + "MinRaw.Value") && plcNames.Contains(Station.AiHp("Zone1_Speed") + "Coefficient.Value"));
            }

            Steps(20);   // instruments accepted and loaded, springs charging
            Check("PLC program version read and as expected (" + c.PlcVersion + ")",
                  c.PlcVersion == AppVersion.ExpectedPlcVersion && c.GetBool("PLC_VERSION_OK") && c.GetString("PLC_VERSION_TEXT") == "PLC " + AppVersion.ExpectedPlcVersion);

            // Zone 1 takes the 37 kW VFD; zone 2 must not get the same source
            c.WriteParameter(Hp(1) + "PowerSource.Value", Ph(1) + "PowerSource.Value", (short)4);
            Steps(15);
            Check("zone 1 connects source 4", c.GetInt(Ph(1) + "PowerSource.Value") == 4 && c.GetInt(Ph(1) + "PowerState") == Station.StateConnected);
            c.WriteParameter(Hp(2) + "PowerSource.Value", Ph(2) + "PowerSource.Value", (short)4);
            Steps(3);
            Check("zone 2 cannot select source 4", c.GetInt(Ph(2) + "PowerSource.Value") == 0 && c.GetBool(Ph(2) + "SelectionRejected"));
            // Every zone has its own flowmeters: zone 2 may take its FM1 while zone 1 uses its FM1
            c.WriteParameter(Hp(2) + "Flowmeter.Value", Ph(2) + "Flowmeter.Value", (short)1);
            Steps(3);
            Check("zones 1 and 2 use their own FM1 at the same time",
                  c.GetInt(Ph(1) + "Flowmeter.Value") == 1 && c.GetInt(Ph(2) + "Flowmeter.Value") == 1 && !c.GetBool(Ph(2) + "SelectionRejected"));

            // The PLC never writes Hp (BluePrism FB_Hmi_*): zone 2's Hp still holds the refused source 4, yet entering 4
            // again once zone 1 has let it go must count as an entry
            c.WriteParameter(Hp(1) + "PowerSource.Value", Ph(1) + "PowerSource.Value", (short)3);
            Steps(15);
            c.WriteParameter(Hp(2) + "PowerSource.Value", Ph(2) + "PowerSource.Value", (short)4);
            Steps(15);
            Check("a refused selection can be entered again once it is free",
                  c.GetInt(Ph(1) + "PowerSource.Value") == 3 && c.GetInt(Ph(2) + "PowerSource.Value") == 4
                  && c.GetInt(Ph(2) + "PowerState") == Station.StateConnected);
            c.WriteParameter(Hp(2) + "PowerSource.Value", Ph(2) + "PowerSource.Value", (short)0);
            Steps(15);
            c.WriteParameter(Hp(1) + "PowerSource.Value", Ph(1) + "PowerSource.Value", (short)4);
            Steps(15);
            c.WriteParameter(Hp(2) + "Flowmeter.Value", Ph(2) + "Flowmeter.Value", (short)2);
            Steps(3);
            Check("selections restored (zone 1 source 4 + FM1, zone 2 FM2)",
                  c.GetInt(Ph(1) + "PowerSource.Value") == 4 && c.GetInt(Ph(1) + "PowerState") == Station.StateConnected
                  && c.GetInt(Ph(1) + "Flowmeter.Value") == 1 && c.GetInt(Ph(2) + "Flowmeter.Value") == 2);

            // Sampling needs a test; the pump does not
            Press(Sim(1) + "PendantTakeSample");
            Check("no sample without a test", c.GetInt(Ph(1) + "SampleCount") == 0);
            Press(Sim(1) + "PendantStart");
            Steps(40);
            Check("pump runs without a test", c.GetBool(Ph(1) + "PumpRunning"));
            Check("start button lit while the pump runs, stop button dark", Lamp(1, "LampStart") && !Lamp(1, "LampStop"));
            Check("power source locked while running", !c.GetBool(Ph(1) + "PowerSource.Enabled"));
            // Only the power source: the flowmeter can be changed while the pump runs
            c.WriteParameter(Hp(1) + "Flowmeter.Value", Ph(1) + "Flowmeter.Value", (short)2);
            Steps(3);
            bool flowmeterChanged = c.GetInt(Ph(1) + "Flowmeter.Value") == 2 && c.GetBool(Ph(1) + "Flowmeter.Enabled") && c.GetBool(Ph(1) + "PumpRunning");
            c.WriteParameter(Hp(1) + "Flowmeter.Value", Ph(1) + "Flowmeter.Value", (short)1);
            Steps(3);
            Check("flowmeter can be changed while running", flowmeterChanged && c.GetInt(Ph(1) + "Flowmeter.Value") == 1);

            // Rotation test before the test: with the pump stopped, run it for the set time, then confirm the direction
            c.Click(Hp(1) + "Stop");
            Steps(30);
            Check("stop button lit while the pump is stopped, start button dark", Lamp(1, "LampStop") && !Lamp(1, "LampStart"));
            Check("start test locked until the rotation is confirmed",
                  !c.GetBool(Ph(1) + "StartTest.Enabled") && c.GetInt(Ph(1) + "RotationState") == Station.RotationNone
                  && c.GetBool(Ph(1) + "RotationTest.Enabled"));
            c.WriteParameter(Hp(1) + "RotationTestTime.Value", Ph(1) + "RotationTestTime.Value", 2.0f);
            Steps(3);
            c.Click(Hp(1) + "RotationTest");
            Steps(12);
            Check("rotation test runs the pump (" + c.GetDouble(Ph(1) + "RotationRemaining").ToString("0.0") + " s left)",
                  c.GetInt(Ph(1) + "RotationState") == Station.RotationRunning && c.GetBool(Ph(1) + "PumpRunning")
                  && !c.GetBool(Ph(1) + "ConfirmRotation.Enabled") && !c.GetBool(Ph(1) + "RotationTest.Enabled"));
            Steps(15);
            Check("rotation test stops after its time and waits for confirmation",
                  c.GetInt(Ph(1) + "RotationState") == Station.RotationWaitConfirm && c.GetBool(Ph(1) + "ConfirmRotation.Enabled")
                  && !c.GetBool(Ph(1) + "StartTest.Enabled"));
            Steps(30);
            Check("the pump stops after the rotation test", !c.GetBool(Ph(1) + "PumpRunning"));
            c.Click(Hp(1) + "ConfirmRotation");
            Steps(3);
            Check("confirming the direction enables start test",
                  c.GetInt(Ph(1) + "RotationState") == Station.RotationConfirmed && c.GetBool(Ph(1) + "StartTest.Enabled"));
            Press(Sim(1) + "PendantStart");
            Steps(40);

            c.Click(Hp(1) + "StartTest");
            Steps(3);
            Check("start test", c.GetBool(Ph(1) + "TestActive") && !c.GetBool(Ph(1) + "StartTest.Enabled"));
            int testNo = c.GetInt(Station.Samples(1) + "TestNo");
            Check("the PLC gives the test its number from the retained counter (" + testNo + ")",
                  testNo == 1042 && c.GetInt(Station.NextTestNo) == 1043);
            foreach (var valve in new[] { 100.0, 80.0, 60.0, 40.0, 25.0, 10.0 })
            {
                c.SetValue(Sim(1) + "ValveOpening", (float)valve);
                Steps(50);
                Press(Sim(1) + "PendantTakeSample");
            }
            Check("six samples taken", c.GetInt(Ph(1) + "SampleCount") == 6);
            bool sampleLampOn = Lamp(1, "LampTakeSample");   // pressed 2 scans ago
            Steps(2);   // the HMI's own scan timer also runs while the test waits, so time passes at least this fast
            bool sampleLampStillOn = Lamp(1, "LampTakeSample");
            Steps(15);
            Check("take-sample button lit for a second after the press", sampleLampOn && sampleLampStillOn && !Lamp(1, "LampTakeSample"));
            var live = PumpCalc.Live(c, 1);
            Check("Pp from source 4's meter (Nş " + (live.ElectricalPower?.ToString("0.00") ?? "—") + " kW, Pp " + (live.PumpPower?.ToString("0.00") ?? "—")
                  + " kW, eff " + (live.Efficiency?.ToString("0.0") ?? "—") + " %)",
                  c.GetBool(Station.Meter(4) + "CommOk") && live.Source == 4 && live.PumpPower > 0
                  && live.Efficiency > 10 && live.Efficiency < 95);
            // The live figures follow the Excel report's formulas (row 32: H velocity loss, I Hm, M Nş, N Np, O efficiency)
            {
                var line = PumpCalc.LiveLine(c);
                string m4 = Station.Meter(4) + "General.";
                double u = c.GetDouble(m4 + "Voltage"), i = c.GetDouble(m4 + "Current"), cos = c.GetDouble(m4 + "PowerFactor");
                double q = c.GetDouble(Ph(1) + "Flow"), pd = c.GetDouble(Ph(1) + "DischargePressure"), ps = c.GetDouble(Ph(1) + "SuctionPressure");
                double vel = 16 / (3.6 * 3.6 * 2 * 9.81) * (Math.Pow(q * 1000 / (3.141 * 150 * 150), 2) - Math.Pow(q * 1000 / (3.141 * 200 * 200), 2));
                double hm = (pd - ps) * 10.2 + vel + 0.6;
                double ns = 1.732 * cos * u * i / 1000, load = ns / 45.0 * 100;
                double np = ns * (load <= 25 ? 88.5 : load <= 50 ? 92.1 : load <= 75 ? 93.4 : 93.0) / 100;
                double eta = q / 3.6 * hm / 102 / np * 100;
                Check(string.Format("live Hm, Nş, Pp and efficiency as the report computes them (Hm {0:0.00} m incl. {1:0.000} m velocity loss, eff {2:0.0} %)", live.Head, live.VelocityHead, live.Efficiency),
                      line.SuctionDn == 200 && line.DischargeDn == 150 && vel > 0
                      && Math.Abs(live.Head - hm) < 0.05 && Math.Abs(live.VelocityHead - vel) < 0.01
                      && Math.Abs(live.DischargeHead - pd * 10.2) < 0.01 && Math.Abs(live.SuctionHead + ps * 10.2) < 0.01
                      && Math.Abs(live.DischargeHead + live.SuctionHead + live.VelocityHead + 0.6 - live.Head) < 0.001
                      && Math.Abs(live.ElectricalPower.Value - ns) < 0.05 && Math.Abs(live.PumpPower.Value - np) < 0.05
                      && Math.Abs(live.Efficiency.Value - eta) < 0.5);
                // The meter's power factor is signed by quadrant: Nş takes its magnitude (400 V, 10 A, cos φ 0.85 → 5.889 kW)
                Check("Nş = 1.732 × |cos φ| × U × I / 1000, also for a negative meter power factor",
                      Math.Abs(PumpCalc.ElectricalPower(400, 10, -0.85) - 5.8888) < 0.001 && Math.Abs(PumpCalc.ElectricalPower(400, 10, 0.85) - 5.8888) < 0.001);
            }
            Check("zone voltage and current come from the meter",
                  c.GetDouble(Station.Sample(1, 6) + "Voltage") > 390 && c.GetDouble(Station.Sample(1, 6) + "Current") > 0);
            Check("unused meters read no load", c.GetDouble(Station.Meter(8) + "General.ActivePower") == 0 && c.GetBool(Station.Meter(8) + "CommOk"));
            double q1 = c.GetDouble(Station.Sample(1, 1) + "Flow"), q6 = c.GetDouble(Station.Sample(1, 6) + "Flow");
            double p1 = c.GetDouble(Station.Sample(1, 1) + "DischargePressure"), p6 = c.GetDouble(Station.Sample(1, 6) + "DischargePressure");
            Check(string.Format("throttling lowers flow and raises pressure (Q {0:0.0}→{1:0.0}, Pd {2:0.00}→{3:0.00})", q1, q6, p1, p6), q6 < q1 && p6 > p1);
            Check("Pt1 scaled with its loaded 0-16 bar range", c.GetDouble(Station.AiPh("Zone1_Pt1") + "MaxPv.Value") == 16.0 && p6 < 6.0);

            double fm1 = c.GetDouble(Station.AiPh("Zone1_FM1") + "Pv");
            Check(string.Format("zone 1 flow comes from its own FM1 ({0:0.0}), zone 2 FM1 stays idle", fm1),
                  fm1 > 0 && Math.Abs(c.GetDouble(Ph(1) + "Flow") - fm1) < 0.5 && c.GetDouble(Station.AiPh("Zone2_FM1") + "Pv") < 1.0);
            double torque = c.GetDouble(Station.AiPh("Zone1_Torque") + "Pv"), speed = c.GetDouble(Station.AiPh("Zone1_Speed") + "Pv");
            Check(string.Format("torque meter and speed sensor read ({0:0.0} Nm at {1:0} rpm)", torque, speed), torque > 0 && speed > 100);
            Check("samples hold torque, speed and cos phi (" + c.GetDouble(Station.Sample(1, 6) + "PowerFactor").ToString("0.000") + ")",
                  c.GetDouble(Station.Sample(1, 6) + "Torque") > 0 && c.GetDouble(Station.Sample(1, 6) + "Speed") > 100
                  && c.GetDouble(Station.Sample(1, 6) + "PowerFactor") > 0.5 && c.GetDouble(Station.Sample(1, 6) + "PowerFactor") <= 1.0);
            Check("every zone has a torque channel, none a torque-meter speed channel (IO list V1.8)",
                  c.Tag(Station.AiPh("Zone3_Torque") + "Pv") != null && c.Tag(Station.AiPh("Zone1_TorqueRpm") + "Pv") == null);

            // The tester in the report is whoever is logged in when the test ends
            Await(c.Security.Auth.LoginAsync("admin", TerakkiFramework.Security.UserStore.DefaultAdminPassword));
            var tester = c.Security.User.Account;
            int records = c.Archive.Records.Count;
            c.Click(Hp(1) + "EndTest");
            Steps(3);
            var archived = c.Archive.Records[0];
            Check("archive names the logged-in user as tester (" + archived.Header["TESTI_YAPAN"] + ")",
                  tester != null && archived.Header["TESTI_YAPAN"] == (string.IsNullOrWhiteSpace(tester.DisplayName) ? tester.Username : tester.DisplayName)
                  && archived.Samples[5].Torque > 0 && archived.Samples[5].PowerFactor > 0);
            Check("the archive keeps the PLC's test number (" + archived.TestNo + ")", archived.TestNo == testNo && archived.File.Contains("Deney_" + testNo + "_"));
            c.Security.Auth.Logout("Smoke");

            // The Excel report: the template filled with this test (kept for a look after the run)
            string report = TestReport.Create(archived, Path.Combine(Path.GetTempPath(), "SempaPumpStationSmoke", "Rapor_Smoke.xlsx"));
            string sheetXml = "", chartXml = "", workbookXml = "";
            using (var zip = System.IO.Compression.ZipFile.OpenRead(report))
                foreach (var e in zip.Entries)
                {
                    string text;
                    using (var rd = new StreamReader(e.Open())) text = rd.ReadToEnd();
                    if (e.FullName.StartsWith("xl/worksheets/sheet")) sheetXml += text;
                    else if (e.FullName.StartsWith("xl/charts/chart")) chartXml += text;
                    else if (e.FullName == "xl/workbook.xml") workbookXml += text;
                }
            Check("Excel report filled from the template (tester, flow, torque, charts to row 37, recalculated on open)",
                  sheetXml.Contains(archived.Header["TESTI_YAPAN"]) && System.Text.RegularExpressions.Regex.IsMatch(sheetXml, "<c r=\"D32\"[^>]*><v>[1-9]") && System.Text.RegularExpressions.Regex.IsMatch(sheetXml, "<c r=\"P37\"[^>]*><v>[1-9]")
                  && chartXml.Contains("$D$32:$D$37") && !chartXml.Contains("$D$32:$D$51") && workbookXml.Contains("fullCalcOnLoad=\"1\""));
            // The torque meter's shaft power and the efficiency on it: formulas in R and S, and the same on the report screen
            var torqueSample = archived.Samples[5];
            double shaft = torqueSample.Torque * torqueSample.Speed / 9549.0;
            Check(string.Format("report: Nmil = T × n / 9549 ({0:0.00} kW) and the efficiency on it ({1:0.0} %); test number at AU1",
                                torqueSample.ShaftPower ?? 0, torqueSample.ShaftEfficiency ?? 0),
                  sheetXml.Contains(">IFERROR(P32*Q32/9549,0)</f>") && sheetXml.Contains("IFERROR(((C32*I32/102)/R32)*100,0)")
                  && System.Text.RegularExpressions.Regex.IsMatch(sheetXml, "<c r=\"AU1\"[^>]*><v>" + archived.TestNo + "</v>")
                  && torqueSample.ShaftPower.HasValue && Math.Abs(torqueSample.ShaftPower.Value - shaft) < 1e-9
                  && torqueSample.ShaftEfficiency.HasValue && Math.Abs(torqueSample.ShaftEfficiency.Value - torqueSample.HydraulicPower / shaft * 100) < 1e-6);
            Check("end test archives the samples", c.Archive.Records.Count == records + 1 && c.Archive.Records[0].Samples.Count == 6);
            Check("samples stay after end test", c.GetInt(Ph(1) + "SampleCount") == 6);
            Check("end test clears the rotation confirmation",
                  c.GetInt(Ph(1) + "RotationState") == Station.RotationNone && !c.GetBool(Ph(1) + "StartTest.Enabled"));

            // A position cannot take a device mounted elsewhere
            c.WriteParameter(Station.PositionHp("Zone3_Pt1") + "Device.Value", Station.PositionPh("Zone3_Pt1") + "Device.Value", (short)3);   // device 3 is on Zone1_Pt1
            Steps(3);
            Check("device mounted twice is rejected", c.GetInt(Station.PositionPh("Zone3_Pt1") + "Device.Value") == 9
                                                     && c.GetBool(Station.PositionPh("Zone3_Pt1") + "SelectionRejected"));
            Check("position can be changed while its zone runs", c.GetBool(Station.PositionPh("Zone1_Pt1") + "Device.Enabled"));

            // Zone 3: 560 kW magnetic switch with spring, then E-stop
            c.WriteParameter(Hp(3) + "PowerSource.Value", Ph(3) + "PowerSource.Value", (short)7);
            // The spring-charged contact is powered through the charge output: the output must stay on while the
            // switch is open (a charge output that drops once charged makes the signal, and the output, flicker)
            int chargeSwitchings = 0;
            bool chargeBefore = c.Plc.Field.ChargeOutput(3, 7);
            for (int i = 0; i < 60; i++)
            {
                Steps(1);
                bool charge = c.Plc.Field.ChargeOutput(3, 7);
                if (charge != chargeBefore) chargeSwitchings++;
                chargeBefore = charge;
            }
            Check("zone 3 magnetic switch closes after spring charge", c.GetInt(Ph(3) + "PowerState") == Station.StateConnected);
            Check("charge-spring output holds while open, off once closed (" + chargeSwitchings + " switchings)",
                  chargeSwitchings <= 1 && !c.Plc.Field.ChargeOutput(3, 7));
            c.SetValue(Sim(3) + "EStopPressed", true);
            Steps(5);
            Check("E-stop latches and opens the switch", c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") != Station.StateConnected);
            Check("the E-stop is not taken for a manipulation; its lamp is off while pressed",
                  c.GetInt(Ph(3) + "ManipulatedDevice") == 0 && !c.GetBool(Ph(1) + "SafetyStop") && !Lamp(3, "LampEStop") && Lamp(3, "LampPanelEStop"));
            c.Click(Hp(3) + "ResetFault");
            Steps(3);
            Check("reset ignored while E-stop pressed", c.GetBool(Ph(3) + "EStop"));
            c.SetValue(Sim(3) + "EStopPressed", false);
            c.Click(Hp(3) + "ResetFault");
            Steps(60);
            Check("released + reset clears E-stop and reconnects", !c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") == Station.StateConnected
                  && Lamp(3, "LampEStop"));

            // The panel E-stop stops the zone like the pendant one
            c.SetValue(Sim(3) + "PanelEStopPressed", true);
            Steps(5);
            Check("panel E-stop latches the zone's E-stop, its lamp off while pressed",
                  c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") != Station.StateConnected && !Lamp(3, "LampPanelEStop")
                  && Lamp(3, "LampEStop") && c.GetInt(Ph(3) + "ManipulatedDevice") == 0);
            c.SetValue(Sim(3) + "PanelEStopPressed", false);
            c.Click(Hp(3) + "ResetFault");
            Steps(60);
            Check("released + reset clears it", !c.GetBool(Ph(3) + "EStop") && c.GetInt(Ph(3) + "PowerState") == Station.StateConnected && Lamp(3, "LampPanelEStop"));

            // Zone 1 drive fault stops the pump
            c.SetValue(Station.SimDrive(4) + "Fault", true);
            Steps(3);
            Check("drive fault stops the pump", c.GetBool(Ph(1) + "DriveFault") && !c.GetBool(Station.SimDrive(4) + "Running"));
            c.SetValue(Station.SimDrive(4) + "Fault", false);
            Press(Sim(1) + "PendantStart");
            Steps(40);

            // Drive feedback watchdog: zone 1's drive (source 4) stops reporting running. After 2 s without the signal
            // the zone faults, its run request drops and with it the drive's run output; start is refused until reset.
            {
                string runDo = "MaintenancePh.Io.DigitalOutputs.Drive_37Vfd_Run.Pv";
                bool ranBefore = c.GetBool(Ph(1) + "PumpRunning") && c.GetBool(runDo);
                c.SetValue(Station.SimDrive(4) + "NoFeedback", true);
                Steps(15);   // 1.5 s
                bool noFaultYet = !c.GetBool(Ph(1) + "DriveFeedbackFault") && c.GetBool(runDo);
                Steps(8);
                Check("no running signal for 2 s: drive feedback fault, run output off, red lamp",
                      ranBefore && noFaultYet && c.GetBool(Ph(1) + "DriveFeedbackFault") && !c.GetBool(runDo) && c.GetBool(Ph(1) + "LampRed"));
                c.SetValue(Station.SimDrive(4) + "NoFeedback", false);
                Press(Sim(1) + "PendantStart");
                Steps(5);
                Check("start is refused until the feedback fault is reset", !c.GetBool(runDo) && c.GetBool(Ph(1) + "DriveFeedbackFault"));
                c.Click(Hp(1) + "ResetFault");
                Steps(3);
                Press(Sim(1) + "PendantStart");
                Steps(40);
                Check("reset clears it and the pump starts again", !c.GetBool(Ph(1) + "DriveFeedbackFault") && c.GetBool(Ph(1) + "PumpRunning"));
            }

            // Someone closes a contactor of zone 3 by hand: every motor stops at once, also zone 1's
            bool zone1Ran = c.GetBool(Ph(1) + "PumpRunning");
            c.Plc.Field.HoldByHand(3, 1, true);
            Steps(2);   // the field changes at the end of a scan; the PLC sees it on the next one
            Check("a contactor closed by hand drops every drive run in the scan that sees it",
                  zone1Ran && c.GetInt(Ph(3) + "ManipulatedDevice") == 1 && !c.GetBool(Station.SimDrive(4) + "Running"));
            Steps(5);
            Check("every zone shows the stop (zone 1 stopped, red lamp)",
                  c.GetBool(Ph(1) + "SafetyStop") && c.GetBool(Ph(2) + "SafetyStop") && c.GetBool(Ph(3) + "SafetyStop")
                  && !c.GetBool(Ph(1) + "PumpRunning") && c.GetBool(Ph(1) + "LampRed") && c.GetInt(Ph(1) + "ManipulatedDevice") == 0);
            Press(Sim(1) + "PendantStart");
            Steps(20);
            Check("no motor starts while the stop holds", !c.GetBool(Ph(1) + "PumpRunning"));
            Check("break-before-make opened zone 3's switch; zone 1's closed contactor stays closed",
                  !c.Plc.Field.DeviceClosed(3, 7) && c.Plc.Field.DeviceClosed(1, 4));
            c.Click(Hp(3) + "ResetFault");
            Steps(3);
            Check("reset ignored while the contactor is still held", c.GetInt(Ph(3) + "ManipulatedDevice") == 1 && c.GetBool(Ph(1) + "SafetyStop"));
            c.Plc.Field.HoldByHand(3, 1, null);
            Steps(50);   // contactor drops; long enough for the switch's spring to charge
            Check("released: nothing closes again before the reset (zone 3's switch stays open)",
                  !c.Plc.Field.DeviceClosed(3, 1) && !c.Plc.Field.DeviceClosed(3, 7) && c.GetInt(Ph(3) + "PowerState") != Station.StateConnected
                  && c.GetBool(Ph(1) + "SafetyStop"));
            c.Click(Hp(3) + "ResetFault");
            Steps(3);
            Check("released + reset in its zone lifts the stop everywhere",
                  c.GetInt(Ph(3) + "ManipulatedDevice") == 0 && !c.GetBool(Ph(1) + "SafetyStop") && !c.GetBool(Ph(3) + "SafetyStop"));
            Steps(40);
            Check("after the reset zone 3's switch closes again", c.GetInt(Ph(3) + "PowerState") == Station.StateConnected);
            Press(Sim(1) + "PendantStart");
            Steps(40);
            Check("zone 1 starts again", c.GetBool(Ph(1) + "PumpRunning"));

            // The closed 560 kW switch of zone 3 is opened by hand (or trips). Counted in exact scans: the HMI's own scan
            // timer is paused, so no extra scan slips in between the steps
            c.ScanTimerEnabled = false;
            c.Plc.Field.HoldByHand(3, 7, false);
            Steps(2);
            bool notYet = c.GetInt(Ph(3) + "ManipulatedDevice") == 0;
            Steps(3);
            Check("a closed switch opened by hand stops every motor once it lasts 200 ms",
                  notYet && c.GetInt(Ph(3) + "ManipulatedDevice") == 7 && !c.GetBool(Station.SimDrive(4) + "Running"));
            c.ScanTimerEnabled = true;
            c.Plc.Field.HoldByHand(3, 7, null);
            Steps(40);   // the switch stays open: nothing closes before the reset
            c.Click(Hp(3) + "ResetFault");
            Steps(60);   // spring charges, the switch closes again
            Check("after reset the switch reconnects and the stop is lifted (" + Station.DeviceText(7) + ")",
                  c.GetInt(Ph(3) + "ManipulatedDevice") == 0 && !c.GetBool(Ph(1) + "SafetyStop") && c.GetInt(Ph(3) + "PowerState") == Station.StateConnected);
            Press(Sim(1) + "PendantStart");
            Steps(40);
            Check("zone 1 runs again", c.GetBool(Ph(1) + "PumpRunning"));

            // Zone 2 runs too, for the overview captures
            c.WriteParameter(Hp(2) + "PowerSource.Value", Ph(2) + "PowerSource.Value", (short)6);
            Steps(15);
            c.Click(Hp(2) + "RotationTest");   // default 3 s
            Steps(65);
            c.Click(Hp(2) + "ConfirmRotation");
            Steps(3);
            Check("zone 2 rotation confirmed before its test", c.GetInt(Ph(2) + "RotationState") == Station.RotationConfirmed);
            c.Click(Hp(2) + "StartTest");
            Press(Sim(2) + "PendantStart");
            Steps(40);
            Press(Sim(2) + "PendantTakeSample");
            Steps(10);

            // Recipes are XML files; a recipe saved as .txt by an older version still loads and becomes XML when saved
            string recipeRoot = RecipeStore.Root;
            RecipeStore.Root = Path.Combine(Path.GetTempPath(), "SempaPumpStationSmoke", "Recipes");
            if (Directory.Exists(RecipeStore.Root)) Directory.Delete(RecipeStore.Root, true);
            try
            {
                RecipeStore.Save("Motor", "Volt 45 kW / IE3", new Dictionary<string, string> { { "MOTOR_GUCU", "45" }, { "MOTOR_MARKA", "Volt <Motor> & Co" } },
                                 new Dictionary<string, string> { { "MOTOR_GUCU", "Gücü (kW)" } });
                string file = Directory.GetFiles(Path.Combine(RecipeStore.Root, "Motor")).Single();
                var xml = System.Xml.Linq.XDocument.Load(file);
                var back = RecipeStore.Load("Motor", "Volt 45 kW / IE3");
                File.WriteAllLines(Path.Combine(RecipeStore.Root, "Motor", "Eski.txt"), new[] { "MOTOR_GUCU=7.5" });
                bool legacyListed = RecipeStore.List("Motor").SequenceEqual(new[] { "Eski", "Volt 45 kW / IE3" });
                bool legacyLoads = RecipeStore.Load("Motor", "Eski")["MOTOR_GUCU"] == "7.5";
                RecipeStore.Save("Motor", "Eski", RecipeStore.Load("Motor", "Eski"));
                Check("recipes saved as XML and read back; old .txt recipes still load and turn into XML",
                      file.EndsWith(".xml") && xml.Root.Name == "Recipe" && (string)xml.Root.Attribute("Name") == "Volt 45 kW / IE3"
                      && back["MOTOR_GUCU"] == "45" && back["MOTOR_MARKA"] == "Volt <Motor> & Co" && legacyListed && legacyLoads
                      && File.Exists(Path.Combine(RecipeStore.Root, "Motor", "Eski.xml")) && !File.Exists(Path.Combine(RecipeStore.Root, "Motor", "Eski.txt")));
            }
            finally { RecipeStore.Root = recipeRoot; }
        }

        // ---------------------------------------------------------------- IO-Link
        // Runs after the station scenario, which leaves zones 1 and 2 running and zone 3 stopped
        private void RunIoLinkScenario(System.Text.StringBuilder sb, ShellViewModel vm)
        {
            var c = _context;
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);
            int Raw(string channel) => c.GetInt(Station.AiPh(channel) + "RawValue");
            int Hub(int port) => c.GetInt(IoLinkLayout.Raw(3, IoLinkModule.AnalogHub, port));
            string Ph(string p) => Station.PositionPh(p);

            // Zone 3 is stopped, so every transmitter reads 0 bar; unmount Pt2's device to tell its port apart
            int pt2Device = c.GetInt(Ph("Zone3_Pt2") + "Device.Value");
            c.WriteParameter(Station.PositionHp("Zone3_Pt2") + "Device.Value", Station.PositionPh("Zone3_Pt2") + "Device.Value", (short)0);
            Steps(3);
            Check("transmitters read their fixed ports", Raw("Zone3_Pt1") == Hub(0) && Raw("Zone3_Pt2") == Hub(1)
                                                         && Hub(1) == IoLinkLayout.OpenInputRaw && Hub(0) != Hub(1));
            Check("fixed sensors read their IO list ports", Raw("Zone3_Speed") == c.GetInt(IoLinkLayout.Raw(3, IoLinkModule.RtdHub, 1)));
            int Port(int zone, IoLinkModule hub, int port) => c.GetInt(IoLinkLayout.Raw(zone, hub, port));
            // IO list V1.9: every instrument on the analog hub; the BNI00AJ carries torque, speed, sound, water and ambient
            Check("IO list V1.9 ports: zone 3 Pt1-2, Vakum1-2, FM1-4 on BNI00KM 0-7; zone 1 Pt1, Vakum1, FM1-2 on BNI0007 0-3; BNI00AJ torque/speed/sound/water/ambient 0/1/2/4/5",
                  Raw("Zone3_Pt2") == Port(3, IoLinkModule.AnalogHub, 1) && Raw("Zone3_Vacuummeter1") == Port(3, IoLinkModule.AnalogHub, 2)
                  && Raw("Zone3_Vacuummeter2") == Port(3, IoLinkModule.AnalogHub, 3)
                  && Raw("Zone3_FM1") == Port(3, IoLinkModule.AnalogHub, 4) && Raw("Zone3_FM4") == Port(3, IoLinkModule.AnalogHub, 7)
                  && Raw("Zone1_Pt1") == Port(1, IoLinkModule.AnalogHub, 0) && Raw("Zone1_Vacuummeter1") == Port(1, IoLinkModule.AnalogHub, 1)
                  && Raw("Zone1_FM1") == Port(1, IoLinkModule.AnalogHub, 2) && Raw("Zone1_FM2") == Port(1, IoLinkModule.AnalogHub, 3)
                  && Raw("Zone1_Torque") == Port(1, IoLinkModule.RtdHub, 0) && Raw("Zone3_Speed") == Port(3, IoLinkModule.RtdHub, 1)
                  && Raw("Zone2_Sound") == Port(2, IoLinkModule.RtdHub, 2) && Raw("Zone2_WaterTemperature") == Port(2, IoLinkModule.RtdHub, 4)
                  && Raw("Zone1_AmbientTemperature") == Port(1, IoLinkModule.RtdHub, 5));
            var z3Analog = IoLinkLayout.Ports(3, IoLinkModule.AnalogHub);
            var z3Rtd = IoLinkLayout.Ports(3, IoLinkModule.RtdHub);
            var z1Analog = IoLinkLayout.Ports(1, IoLinkModule.AnalogHub);
            var z1Rtd = IoLinkLayout.Ports(1, IoLinkModule.RtdHub);
            Check("IO-Link page labels follow the V1.9 sheet (" + Station.Positions.Length + " instrument positions)",
                  Station.Positions.Length == 20 && Station.FlowmeterCount(1) == 2 && Station.FlowmeterCount(3) == 4
                  && Station.TransmitterCount(1) == 1 && Station.TransmitterCount(2) == 2 && Station.VacuummeterCount(1) == 1 && Station.VacuummeterCount(3) == 2
                  && z3Analog[1].Position == "Zone3_Pt2" && z3Analog[2].Position == "Zone3_Vacuummeter1" && z3Analog[3].Label == "Vakummetre 2"
                  && z3Analog[4].Position == "Zone3_FM1" && z3Analog[7].Position == "Zone3_FM4"
                  && z3Rtd[0].Label == "Tork" && z3Rtd[1].Label == "Devir sayısı" && z3Rtd[2].Label == "Ses seviyesi" && z3Rtd[3].Label == ""
                  && z3Rtd[4].Channel == "Zone3_WaterTemperature" && z3Rtd[6].Label == "" && IoLinkLayout.TypeName(3, IoLinkModule.AnalogHub) == "BNI00KM"
                  && z1Analog[0].Position == "Zone1_Pt1" && z1Analog[1].Position == "Zone1_Vacuummeter1" && z1Analog[3].Position == "Zone1_FM2"
                  && !z1Analog[4].Present && z1Rtd[0].Label == "Tork" && z1Rtd[5].Channel == "Zone1_AmbientTemperature"
                  && IoLinkLayout.TypeName(1, IoLinkModule.AnalogHub) == "BNI0007");

            // The device on a port is chosen on the IO-Link screen
            var screen = (Screens.IoLinkViewModel)System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "IoLink").Screen;
            screen.Zones[2].Command.Execute(null);
            screen.Hubs[1].Ports[1].SelectCommand.Execute(null);   // BNI00KM port 1 = Pt2
            Steps(2);
            var free = System.Linq.Enumerable.First(screen.Devices, d => d.Value > 0 && d.State == "");
            var held = System.Linq.Enumerable.First(screen.Devices, d => d.State == "held");
            free.Command.Execute(null);
            Steps(3);
            Check("a device chosen on the port is mounted", screen.CanAssign && c.GetInt(Ph("Zone3_Pt2") + "Device.Value") == free.Value);
            held.Command.Execute(null);
            Steps(3);
            Check("a device mounted elsewhere is refused", c.GetInt(Ph("Zone3_Pt2") + "Device.Value") == free.Value
                                                           && c.GetBool(Ph("Zone3_Pt2") + "SelectionRejected"));
            c.WriteParameter(Station.PositionHp("Zone3_Pt2") + "Device.Value", Station.PositionPh("Zone3_Pt2") + "Device.Value", (short)pt2Device);
            Steps(3);
            Check("restored", c.GetInt(Ph("Zone3_Pt2") + "Device.Value") == pt2Device);

            screen.Zones[0].Command.Execute(null);
            screen.Hubs[1].Ports[0].SelectCommand.Execute(null);   // zone 1 Pt1, zone 1 runs
            Steps(2);
            Check("the device can be changed while the zone runs", screen.CanAssign && c.GetBool(Station.ZonePh(1) + "PumpRunning"));

            // No Yükle: while zone 1 runs, an edited library entry reaches the channel at once, range and filter alike
            {
                var instruments = (Screens.InstrumentsViewModel)System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "Instruments").Screen;
                var row = System.Linq.Enumerable.First(instruments.Positions, r => r.Name == "Zone1_Pt1");
                string entry = Station.LibraryEntry(Station.PressureTransmitter, c.GetInt(Ph("Zone1_Pt1") + "Device.Value"));
                string ai = Station.AiPh("Zone1_Pt1");
                int filterType = c.GetInt(entry + "FilterType");
                double maxPv = c.GetDouble(entry + "MaxPv");
                c.SetValue(entry + "FilterType", (short)2);
                c.SetValue(entry + "MaxPv", (float)(maxPv + 10));
                Steps(3);
                Check("an edited library entry applies at once while the zone runs (filter " + c.GetInt(ai + "FilterType.Value") + ", max " + c.GetDouble(ai + "MaxPv.Value") + ")",
                      c.GetBool(Station.ZonePh(1) + "PumpRunning")
                      && c.GetInt(ai + "FilterType.Value") == 2 && System.Math.Abs(c.GetDouble(ai + "MaxPv.Value") - (maxPv + 10)) < 1e-3);
                c.SetValue(entry + "FilterType", (short)filterType);
                c.SetValue(entry + "MaxPv", (float)maxPv);
                Steps(3);
                Check("and back", c.GetInt(ai + "FilterType.Value") == filterType && System.Math.Abs(c.GetDouble(ai + "MaxPv.Value") - maxPv) < 1e-3);

                // Coefficient, offset and moving average belong to the library device (StationInstrumentAdjustments): set in the
                // library, they reach the mounted position's channel at once; the position only shows them
                {
                    int device = c.GetInt(Ph("Zone1_Pt1") + "Device.Value");
                    var libraryRow = new Screens.LibraryRow(c, Station.PressureTransmitter, device);
                    libraryRow.Coefficient = 1.5;
                    libraryRow.Offset = 0.2;
                    libraryRow.FilterAverageCount = 10;
                    Steps(3);
                    row.Channel.RefreshParameters();
                    Check(string.Format("library Katsayı / Ofset / Ortalama reach the position's channel at once ({0} / {1} / {2})",
                                        row.Channel.Coefficient, row.Channel.Offset, row.Channel.FilterAverageCount),
                          c.GetDouble(ai + "Coefficient.Value") == 1.5 && Math.Abs(c.GetDouble(ai + "Offset.Value") - 0.2) < 1e-6
                          && c.GetInt(ai + "FilterAverageCount.Value") == 10 && row.Channel.FilterAverageCount == 10);
                    libraryRow.FilterAverageCount = 500;
                    bool limited = libraryRow.FilterAverageCount == 100;
                    libraryRow.Coefficient = 1.0;
                    libraryRow.Offset = 0.0;
                    libraryRow.FilterAverageCount = 0;
                    Steps(3);
                    Check("library average limited to 100; and back to 1 / 0 / off",
                          limited && c.GetDouble(ai + "Coefficient.Value") == 1.0 && c.GetDouble(ai + "Offset.Value") == 0.0 && c.GetInt(ai + "FilterAverageCount.Value") == 0);
                }

                // Signal types: passed by the PLC program, not an HMI parameter; 4…20 mA unless set
                int Sig(string ch) => c.GetInt(Station.AiPh(ch) + "SignalType");
                Check("signal types from the program: torque −10…10 V, speed 0…10 V, every other channel 4…20 mA; not an HMI parameter",
                      Sig("Zone1_Pt1") == AnalogSignal.Current4To20 && Sig("Zone2_FM3") == AnalogSignal.Current4To20 && Sig("Zone3_Vacuummeter2") == AnalogSignal.Current4To20
                      && Sig("Zone1_Torque") == AnalogSignal.VoltageMinus10To10 && Sig("Zone3_Speed") == AnalogSignal.Voltage0To10
                      && Sig("Zone2_Sound") == AnalogSignal.Current4To20 && Sig("Zone1_WaterTemperature") == AnalogSignal.Current4To20
                      && c.Tag(Station.AiHp("Zone1_Torque") + "SignalType.Value") == null);

                // Raw and filtered raw (counts) and the same as the signal: 4 mA at MinRaw, 20 mA at MaxRaw
                int raw = c.GetInt(ai + "RawValue"), minRaw = c.GetInt(ai + "MinRaw.Value"), maxRaw = c.GetInt(ai + "MaxRaw.Value");
                double mA = 4.0 + 16.0 * (raw - minRaw) / (maxRaw - minRaw);
                double filteredMa = 4.0 + 16.0 * (c.GetDouble(ai + "FilteredRaw") - minRaw) / (maxRaw - minRaw);
                row.Channel.RefreshLive();
                Check(string.Format("raw {0}, filtered raw {1}, raw signal {2}, filtered signal {3} ({4})",
                                    row.Channel.Raw, row.Channel.Filtered, row.Channel.RawSignal, row.Channel.FilteredSignal, row.Channel.Signal),
                      System.Math.Abs(c.GetDouble(ai + "RawSignal") - mA) < 0.01 && System.Math.Abs(c.GetDouble(ai + "FilteredSignal") - filteredMa) < 0.01
                      && row.Channel.Raw == raw.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      && row.Channel.Filtered == c.GetDouble(ai + "FilteredRaw").ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                      && row.Channel.RawSignal.EndsWith(" mA") && row.Channel.FilteredSignal.EndsWith(" mA") && row.Channel.Signal == "4…20 mA");

                // Coefficient (0 taken as 1) and offset: Pv = scaled × coefficient + offset
                string snd = Station.AiPh("Zone2_Sound"), sndHp = Station.AiHp("Zone2_Sound");
                double Scaled() => c.GetDouble(snd + "MinPv.Value") + (c.GetDouble(snd + "FilteredRaw") - c.GetInt(snd + "MinRaw.Value"))
                                   / (c.GetInt(snd + "MaxRaw.Value") - c.GetInt(snd + "MinRaw.Value")) * (c.GetDouble(snd + "MaxPv.Value") - c.GetDouble(snd + "MinPv.Value"));
                bool unity = c.GetDouble(snd + "Coefficient.Value") == 1.0 && c.GetDouble(snd + "Offset.Value") == 0.0 && System.Math.Abs(c.GetDouble(snd + "Pv") - Scaled()) < 0.01;
                c.WriteParameter(sndHp + "Coefficient.Value", snd + "Coefficient.Value", 2.0f);
                c.WriteParameter(sndHp + "Offset.Value", snd + "Offset.Value", 5.0f);
                Steps(3);
                Check(string.Format("coefficient 1 / offset 0 by default; × 2 + 5 applies at once ({0:0.00} = {1:0.00} × 2 + 5)", c.GetDouble(snd + "Pv"), Scaled()),
                      unity && System.Math.Abs(c.GetDouble(snd + "Pv") - (Scaled() * 2.0 + 5.0)) < 0.02);
                c.WriteParameter(sndHp + "Coefficient.Value", snd + "Coefficient.Value", 0.0f);
                c.WriteParameter(sndHp + "Offset.Value", snd + "Offset.Value", 0.0f);
                Steps(3);
                Check("a coefficient of 0 is taken as 1", c.GetDouble(snd + "Coefficient.Value") == 1.0 && System.Math.Abs(c.GetDouble(snd + "Pv") - Scaled()) < 0.01);

                // The channel's moving average (FB_Io_AnalogInput): the mean of the last n filtered values, after the filter;
                // a standalone port instance on the sound channel, fed known raw values
                {
                    var pt = new Infrastructure.Plc.PlcTags(c.Tags);
                    var ch = new Infrastructure.Plc.AnalogInputChannel("Zone2_Sound", 0, 100);
                    ch.FilterAverageCount.RetainValue = 4;
                    var seen = new List<double>();
                    foreach (int r in new[] { 1000, 2000, 3000, 4000, 5000 })
                    {
                        ch.Run(pt, r);
                        seen.Add(c.GetDouble(snd + "FilteredRaw"));
                    }
                    Check(string.Format("moving average over 4 values: {0}", string.Join(" / ", seen.Select(v => v.ToString("0")))),
                          seen[0] == 1000 && seen[1] == 1500 && seen[3] == 2500 && seen[4] == 3500
                          && Math.Abs(ch.Pv - 3500.0 / 27648 * 100) < 1e-6);
                    ch.FilterAverageCount.Value = 0;
                    ch.Run(pt, 6000);
                    Check("average 0 is off", c.GetDouble(snd + "FilteredRaw") == 6000);

                    var zero = new Infrastructure.Plc.AnalogInputChannel("Zone2_Sound", 0, 100);
                    zero.MaxRaw.RetainValue = 0;
                    zero.FilterEmaAlpha.RetainValue = 0;
                    zero.FilterOutlierMaxJump.RetainValue = 0;
                    zero.FilterOutlierMaxRejectCount.RetainValue = 0;
                    zero.FilterAverageCount.RetainValue = 500;
                    zero.Run(pt, 1000);
                    Check("parameters left at 0 by a reinitialising download fall back to the defaults (Max Ham 27648, EMA α 0.2, jump 1000, reject 5; average limited to 100)",
                          zero.MaxRaw.Value == 27648 && zero.FilterEmaAlpha.Value == 0.2 && zero.FilterOutlierMaxJump.Value == 1000.0
                          && zero.FilterOutlierMaxRejectCount.Value == 5 && zero.FilterAverageCount.Value == 100);
                    Steps(3);   // the station's own sound channel writes its values again
                }

                // The average set on Analog Ayarları reaches the channel
                {
                    var settingsVm = (Screens.AnalogSettingsViewModel)System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "AnalogSettings").Screen;
                    var soundRow = System.Linq.Enumerable.First(settingsVm.Rows, r => r.Channel == "Zone2_Sound");
                    soundRow.FilterAverageCount = 20;
                    Steps(4);
                    bool set = c.GetInt(snd + "FilterAverageCount.Value") == 20 && soundRow.FilterAverageCount == 20;
                    soundRow.FilterAverageCount = 0;
                    Steps(4);
                    Check("Analog Ayarları writes the channel's average (20, then off)", set && c.GetInt(snd + "FilterAverageCount.Value") == 0);
                }
            }

            // Energy meters: a 3-wire system (no line-to-neutral values) and one filter per meter
            {
                string m4 = Station.Meter(4);
                Check(string.Format("3-wire meter: phases carry line-to-line voltages ({0:0.0} V) and currents, no phase powers; totals as before",
                                    c.GetDouble(m4 + "PhaseA.Voltage")),
                      c.GetDouble(m4 + "PhaseA.Voltage") > 390 && c.GetDouble(m4 + "PhaseC.Voltage") > 390 && c.GetDouble(m4 + "PhaseB.Current") > 0
                      && c.GetDouble(m4 + "PhaseA.ActivePower") == 0 && c.GetDouble(m4 + "PhaseB.PowerFactor") == 0
                      && c.GetDouble(m4 + "General.ActivePower") > 0 && c.GetDouble(m4 + "General.PowerFactor") > 0);

                var meters = (Screens.PowermetersViewModel)System.Linq.Enumerable.First(vm.DiagNav.Concat(vm.MainNav), e => e.Key == "Powermeters").Screen;
                var card = meters.Meters.First(m => m.Source == 4);
                Check("Enerji Analizörleri: phase lines without powers and cos φ, the total line with them",
                      card.Rows[0].ActivePower == "" && card.Rows[0].PowerFactor == "" && card.Rows[0].Voltage.Length > 0
                      && card.Rows[3].ActivePower.Length > 0 && card.Rows[3].PowerFactor.Length > 0);

                string fph = Station.MeterFilterPh(4);
                Check("meter filter defaults: none, EMA α 0.2, jump 20 %, reject 5, average off; the three filter types offered",
                      c.GetInt(fph + "FilterType.Value") == 0 && Math.Abs(c.GetDouble(fph + "FilterEmaAlpha.Value") - 0.2) < 1e-6
                      && c.GetDouble(fph + "FilterOutlierMaxJump.Value") == 20.0 && c.GetInt(fph + "FilterOutlierMaxRejectCount.Value") == 5
                      && c.GetInt(fph + "FilterAverageCount.Value") == 0 && card.Filter.AverageText == "Kapalı"
                      && AnalogFilter.MeterItems.Length == 3);
                card.Filter.FilterType = AnalogFilter.EmaOutlier;
                c.WriteParameter(card.Filter.AverageWriteTag, card.Filter.AverageTag, (short)20);
                c.WriteParameter(card.Filter.JumpWriteTag, card.Filter.JumpTag, 15.0f);
                Steps(4);
                Check("the screen writes the meter's filter (EMA + outlier, jump 15 %, average 20 = " + card.Filter.AverageText + ")",
                      c.GetInt(fph + "FilterType.Value") == 2 && c.GetInt(fph + "FilterAverageCount.Value") == 20
                      && c.GetDouble(fph + "FilterOutlierMaxJump.Value") == 15.0 && card.Filter.AverageText == "20 (2.0 s)");
                card.Filter.FilterType = AnalogFilter.None;
                c.WriteParameter(card.Filter.AverageWriteTag, card.Filter.AverageTag, (short)0);
                c.WriteParameter(card.Filter.JumpWriteTag, card.Filter.JumpTag, 20.0f);
                Steps(4);

                // The filter itself (port of FB_Communication_PowermeterFilter), on known values: EMA α 0.5, then a 4-value average
                var pt = new Infrastructure.Plc.PlcTags(c.Tags);
                var f = new Infrastructure.Plc.PowermeterFilter(8);
                f.FilterType.RetainValue = AnalogFilter.Ema;
                f.FilterEmaAlpha.RetainValue = 0.5;
                double[] V(double v) => Enumerable.Repeat(v, Infrastructure.Plc.PowermeterFilter.Values.Length).ToArray();
                double a = f.Run(pt, V(100), true, true)[0];
                double b = f.Run(pt, V(200), true, true)[0];
                double kept = f.Run(pt, V(200), true, false)[0];
                double d = f.Run(pt, V(200), true, true)[0];
                f.FilterAverageCount.Value = 4;
                double avg = f.Run(pt, V(200), true, true)[0];   // EMA 187.5; mean of 100, 150, 175, 187.5
                Check(string.Format("meter EMA takes a value only on the 100 ms pulse ({0} → {1} → {2} → {3}), then the moving average ({4:0.000})", a, b, kept, d, avg),
                      a == 100 && b == 150 && kept == 150 && d == 175 && Math.Abs(avg - 153.125) < 1e-9);
                var lost = f.Run(pt, V(0), false, true);
                double reseeded = f.Run(pt, V(300), true, true)[0];
                var nan = V(300);
                nan[5] = double.NaN;
                var noNan = f.Run(pt, nan, true, true);
                Check("a meter that stops answering passes 0; the filter starts again from its next values; a NaN counts as 0",
                      lost.All(v => v == 0) && reseeded == 300 && !noNan.Any(double.IsNaN));
                f.FilterEmaAlpha.Value = 0;
                f.Run(pt, V(300), true, true);
                Check("a meter's EMA α of 0 falls back to 0.2", f.FilterEmaAlpha.Value == 0.2);

                // EMA + outlier rejection (jump in % of the value): a single spike is ignored, a lasting step is taken after 3 in a row
                var o = new Infrastructure.Plc.PowermeterFilter(8);
                o.FilterType.RetainValue = AnalogFilter.EmaOutlier;
                o.FilterEmaAlpha.RetainValue = 1.0;   // no smoothing, so only the rejection shows
                o.FilterOutlierMaxJump.RetainValue = 20.0;
                o.FilterOutlierMaxRejectCount.RetainValue = 3;
                var seen = new List<double>();
                foreach (double x in new[] { 100.0, 110.0, 300.0, 110.0, 200.0, 200.0, 200.0, 200.0 }) seen.Add(o.Run(pt, V(x), true, true)[0]);
                Check("EMA + outlier: " + string.Join(" → ", seen),
                      seen.SequenceEqual(new[] { 100.0, 110.0, 110.0, 110.0, 110.0, 110.0, 200.0, 200.0 }));
                Steps(3);   // meter 8's own filter writes its values again

                // The 630 kW VFD's ATV660, read over Modbus TCP: stopped here (no zone runs on source 8)
                var drive = meters.Drive;
                string zero = 0.0.ToString("0.0");
                Check(string.Format("ATV660 card: {0}, {1}, {2} Hz, {3} A; status word decoded (running 0x0637, fault bit 3)", drive.Name, drive.Status, drive.Frequency, drive.Current),
                      drive.CommOk && drive.ExtraCommOk && drive.Status == "Duruyor" && drive.Frequency == zero && drive.Current == zero
                      && Screens.DriveCard.StatusText(0x0637) == "Çalışıyor" && Screens.DriveCard.StatusText(0x0608) == "Arıza"
                      && Screens.DriveCard.StatusText(0x0633) == "Duruyor");
                drive.Filter.FilterType = AnalogFilter.EmaOutlier;
                c.WriteParameter(drive.Filter.AverageWriteTag, drive.Filter.AverageTag, (short)10);
                Steps(4);
                Check("the drive has its own filter setting (EMA + outlier, average " + drive.Filter.AverageText + ")",
                      c.GetInt(Station.DriveFilterPh + "FilterType.Value") == 2 && c.GetInt(Station.DriveFilterPh + "FilterAverageCount.Value") == 10
                      && c.GetInt(fph + "FilterType.Value") == 0);
                drive.Filter.FilterType = AnalogFilter.None;
                c.WriteParameter(drive.Filter.AverageWriteTag, drive.Filter.AverageTag, (short)0);
                Steps(4);
            }

            // Suction vacuummeter (IO list V1.9): zone 2 measures with the one selected, also while it runs; zone 1 has one
            {
                string z2hp = Station.ZoneHp(2), z2ph = Station.ZonePh(2), z1hp = Station.ZoneHp(1), z1ph = Station.ZonePh(1);
                c.WriteParameter(z2hp + "Vacuummeter.Value", z2ph + "Vacuummeter.Value", (short)2);
                Steps(4);
                double suction = c.GetDouble(z2ph + "SuctionPressure"), vac2 = c.GetDouble(Station.AiPh("Zone2_Vacuummeter2") + "Pv");
                Check(string.Format("zone 2 suction from the selected Vakum2 ({0:0.000} bar, device {1})", suction, c.GetInt(z2ph + "VacuummeterDevice")),
                      c.GetInt(z2ph + "Vacuummeter.Value") == 2 && Math.Abs(suction - vac2) < 0.05
                      && c.GetInt(z2ph + "VacuummeterDevice") == c.GetInt(Ph("Zone2_Vacuummeter2") + "Device.Value"));
                c.WriteParameter(z1hp + "Vacuummeter.Value", z1ph + "Vacuummeter.Value", (short)2);
                Steps(4);
                Check("zone 1 refuses a second vacuummeter", c.GetInt(z1ph + "Vacuummeter.Value") == 1 && c.GetBool(z1ph + "SelectionRejected"));
                c.WriteParameter(z2hp + "Vacuummeter.Value", z2ph + "Vacuummeter.Value", (short)1);
                Steps(4);
                Check("zone 2 back on Vakum1", c.GetInt(z2ph + "Vacuummeter.Value") == 1);
            }

            // Analog Ayarları: a fixed sensor's filter set on the screen reaches the channel
            {
                var settings = (Screens.AnalogSettingsViewModel)System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "AnalogSettings").Screen;
                var sound = System.Linq.Enumerable.First(settings.Rows, r => r.Channel == "Zone2_Sound");
                int before = sound.FilterType;
                sound.FilterType = 2;
                sound.MaxRaw = 27000;
                Steps(4);
                string ai = Station.AiPh("Zone2_Sound");
                Check("Analog Ayarları writes the sensor's filter and range (" + settings.Rows.Count + " sensors)",
                      settings.Rows.Count == 21 && c.GetInt(ai + "FilterType.Value") == 2 && c.GetInt(ai + "MaxRaw.Value") == 27000
                      && sound.FilterType == 2 && sound.Raw.Length > 0);
                sound.FilterType = before;
                sound.MaxRaw = 27648;
                Steps(4);
            }

            // BNI007Z row by row as in the sheet: 0 tower green/yellow, 1 start, 2 siren/red, 3 stop, 4 free, 5 sample, 6 panel E-stop, 7 E-stop
            var dio = screen.Hubs[2].Ports;
            Check("BNI007Z ports follow the V1.8 sheet row by row (start: " + dio[1].Value + ", stop: " + dio[3].Value + ")",
                  dio[1].Title == "Pompa Başlat / lamba" && dio[1].Value.EndsWith("lamba yanıyor")
                  && dio[2].Title == "Siren / Kırmızı ışık" && dio[3].Title == "Pompa Durdur / lamba" && dio[3].Value.EndsWith("lamba sönük")
                  && dio[4].Title == "Boş" && dio[5].Title == "Örnek al / lamba"
                  && dio[6].Title == "Pano acil stop / lamba" && dio[6].Value.StartsWith("Normal") && dio[7].Title == "EStop / lamba");
        }

        // ---------------------------------------------------------------- digital output forcing
        // Zone 3 is stopped with the 560 kW switch closed; its 7.5 kW soft starter contactor is not requested
        private void RunOutputForceScenario(System.Text.StringBuilder sb, ShellViewModel vm)
        {
            var c = _context;
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);
            var screen = ((Screens.IoViewModel)System.Linq.Enumerable.First(vm.DiagNav, e => e.Key == "Io").Screen).Outputs;
            var row = System.Linq.Enumerable.First(System.Linq.Enumerable.SelectMany(screen.Groups, g => g.Rows), r => r.Name == "Zone3_K7k5Ss_Close");
            string asked = null;
            screen.Confirm = q => { asked = q; return false; };

            row.ForceOnCommand.Execute(null);
            Steps(3);
            Check("forcing asks first, and a 'no' forces nothing", asked != null && !row.Forcing && !row.Pv);

            screen.Confirm = q => true;
            row.ForceOnCommand.Execute(null);
            Steps(10);
            Check("Force ON drives the output and is shown as forced", row.Forcing && row.Pv && screen.ForcedCount == 1);
            Check("the forced output reaches the field (contactor closes)", c.Plc.Field.DeviceClosed(3, 1));
            Check("a contactor forced closed counts as a manipulation: every motor stops",
                  c.GetInt(Station.ZonePh(3) + "ManipulatedDevice") == 1 && c.GetBool(Station.ZonePh(1) + "SafetyStop"));

            row.ForceOffCommand.Execute(null);
            Steps(3);
            Check("Force OFF holds the output at 0", row.Forcing && !row.Pv);

            screen.ReleaseAllCommand.Execute(null);
            Steps(10);
            Check("release returns the output to the program", !row.Forcing && !row.Pv && screen.ForcedCount == 0 && !c.Plc.Field.DeviceClosed(3, 1));

            // The contactor moved without a request: acknowledge the manipulation; zone 3's switch closes again
            c.Click(Station.ZoneHp(3) + "ResetFault");
            Steps(60);
            Check("acknowledged: the stop is lifted and zone 3's switch closes again",
                  !c.GetBool(Station.ZonePh(1) + "SafetyStop") && c.GetInt(Station.ZonePh(3) + "PowerState") == Station.StateConnected);

            // Input watch tables
            var io = (Screens.IoViewModel)System.Linq.Enumerable.First(vm.DiagNav, e => e.Key == "Io").Screen;
            io.ShowTab(Screens.IoViewModel.DigitalInputsTab);
            Steps(2);
            var inputs = System.Linq.Enumerable.ToList(System.Linq.Enumerable.SelectMany(io.Inputs.Groups, g => g.Rows));
            Check("DI watch shows the field (zone 3 560 kW switch closed, not open)",
                  inputs.Count == 85 && inputs.Find(r => r.Name == "Zone3_Q560Ss_Closed").Pv && !inputs.Find(r => r.Name == "Zone3_Q560Ss_Open").Pv);

            // Forcing an input: the program takes the forced value instead of the field. Zone 3's E-stop forced to 0
            // (pressed) opens its closed 560 kW switch; released and reset, the switch closes again
            var estop = inputs.Find(r => r.Name == "Zone3_EStopOk");
            string inputAsked = null;
            io.Inputs.Confirm = q => { inputAsked = q; return true; };
            estop.ForceOffCommand.Execute(null);
            Steps(30);
            var alarmWait = DateTime.Now.AddMilliseconds(1300);   // the alarm monitor scans once a second
            while (DateTime.Now < alarmWait) { DoEvents(); Thread.Sleep(50); }
            Check("an input forced to 0 is used by the program and raises a warning (zone 3 E-stop: switch opened)",
                  inputAsked != null && inputAsked.Contains("girişi") && estop.Forcing && !estop.Pv
                  && c.GetBool(Station.ZonePh(3) + "EStop") && c.Plc.Field.DeviceOpen(3, 7) && io.Inputs.ForcedCount == 1
                  && c.Plc.Field.EStopOk(3)
                  && System.Linq.Enumerable.Any(vm.Alarms.Active, a => a.Message.Contains("Giriş zorlanıyor")));
            io.Inputs.ReleaseAllCommand.Execute(null);
            Steps(5);
            c.Click(Station.ZoneHp(3) + "ResetFault");
            Steps(60);
            Check("released and reset: the input follows the field again, zone 3's switch closes",
                  !estop.Forcing && estop.Pv && io.Inputs.ForcedCount == 0 && !c.GetBool(Station.ZonePh(3) + "EStop")
                  && c.GetInt(Station.ZonePh(3) + "PowerState") == Station.StateConnected);
            io.ShowTab(Screens.IoViewModel.AnalogInputsTab);
            Steps(2);
            var pt2 = System.Linq.Enumerable.First(System.Linq.Enumerable.SelectMany(io.AnalogGroups, g => g.Rows), r => r.Name == "Zone1_Pt1");
            Check("AI watch shows value, raw and range (" + pt2.Value + ", " + pt2.Raw + ", " + pt2.Range + ")",
                  pt2.Value.EndsWith("bar") && pt2.Raw.Length > 0 && pt2.Range.Contains("16"));
            io.ShowTab(Screens.IoViewModel.AnalogOutputsTab);
            Steps(2);
            // Zone 1 runs on the 37 kW VFD: its speed reference is on that drive's output, the unused VFDs read 0
            var ao37 = System.Linq.Enumerable.First(io.AnalogOutputs, r => r.Name == "Drive_37Vfd_SpeedReference");
            var ao630 = System.Linq.Enumerable.First(io.AnalogOutputs, r => r.Name == "Drive_630Vfd_SpeedReference");
            string zone1Speed = c.GetDouble(Station.ZonePh(1) + "SpeedReference.Value").ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %";
            Check("AO watch shows the speed references (" + ao37.Value + " = zone 1 " + zone1Speed + ", " + ao630.Value + ")",
                  io.AnalogOutputs.Count == 4 && ao37.Value == zone1Speed && ao630.Value == "0.0 %" && ao37.Raw != "0");
            io.ShowTab(Screens.IoViewModel.DigitalInputsTab);
        }

        // ---------------------------------------------------------------- login system
        // ---------------------------------------------------------------- PLC reconnect timing
        // A lost link is tried again in rounds: 3 attempts (at 0, 5 and 10 s), a new round every 30 s, until it connects
        private void RunReconnectPolicyScenario(System.Text.StringBuilder sb)
        {
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);
            var p = new ReconnectPolicy(3, 30);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            DateTime At(double s) => t0.AddSeconds(s);
            var attempts = new List<double>();
            for (double s = 0; s <= 75; s += 0.5)
                if (p.Due(At(s)))
                {
                    p.Starting(At(s));
                    attempts.Add(s);
                    p.Failed(At(s));
                }
            Check("reconnect: 3 attempts every 30 s (" + string.Join(", ", attempts) + " s)",
                  attempts.SequenceEqual(new double[] { 0, 5, 10, 30, 35, 40, 60, 65, 70 }));
            bool waiting = p.WaitingForNextRound && p.Attempt == 3;
            p.Reset();
            Check("reconnect: after a successful connect the next loss is tried at once", waiting && p.Due(At(71)) && p.Attempt == 0);
        }

        // ---------------------------------------------------------------- editing a value
        // A click on a parameter box or an editable table cell selects the whole value, so typing replaces it
        public sealed class EditProbeRow { public double Value { get; set; } = 27648; public string Name { get; set; } = "x"; public int Filter { get; set; } }

        private void RunEditScenario(System.Text.StringBuilder sb)
        {
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);
            System.Windows.Input.MouseButtonEventArgs Click(UIElement target) =>
                new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseDownEvent, Source = target };

            var box = new Controls.TagTextBox { Text = "80.5", Width = 120 };
            var grid = new System.Windows.Controls.DataGrid { Style = (Style)FindResource("Dg"), Height = 120, Width = 300 };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Ad", Binding = new System.Windows.Data.Binding("Name"), IsReadOnly = true });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = "Değer", Binding = new System.Windows.Data.Binding("Value") });
            grid.Columns.Add(new System.Windows.Controls.DataGridComboBoxColumn
            {
                Header = "Filtre", ItemsSource = AnalogFilter.Items, SelectedValueBinding = new System.Windows.Data.Binding("Filter"),
                SelectedValuePath = "Value", DisplayMemberPath = "Display",
            });
            grid.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<EditProbeRow> { new EditProbeRow() };   // an array cannot be edited
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBox());   // takes the focus first
            panel.Children.Add(box);
            panel.Children.Add(grid);
            var window = new Window { Content = panel, Width = 400, Height = 300, ShowInTaskbar = false, Left = -2000, Top = -2000 };
            window.Show();
            try
            {
                window.UpdateLayout(); DoEvents();
                box.RaiseEvent(Click(box)); DoEvents();
                Check("a click on a parameter box selects its value (\"" + box.SelectedText + "\")", box.IsKeyboardFocused && box.SelectedText == "80.5");

                var row = (System.Windows.Controls.DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                var cell = (System.Windows.Controls.DataGridCell)grid.Columns[1].GetCellContent(row).Parent;
                cell.RaiseEvent(Click(cell)); DoEvents();
                var editor = grid.Columns[1].GetCellContent(row) as System.Windows.Controls.TextBox;
                Check("one click on a table cell edits it with the value selected (\"" + editor?.SelectedText + "\")",
                      cell.IsEditing && editor != null && editor.SelectedText == "27648");
                if (editor != null) editor.Text = "27000";
                grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);   // as when the next cell of the row is clicked
                DoEvents();
                Check("a value is written when its cell is left (" + ((EditProbeRow)row.Item).Value + ")", ((EditProbeRow)row.Item).Value == 27000);
                var readOnlyCell = (System.Windows.Controls.DataGridCell)grid.Columns[0].GetCellContent(row).Parent;
                readOnlyCell.RaiseEvent(Click(readOnlyCell)); DoEvents();
                Check("a read-only cell does not start editing", !readOnlyCell.IsEditing);

                // The filter type is a choice: one click opens the list, the pick is written when it closes
                var probe = (EditProbeRow)row.Item;
                var filterCell = (System.Windows.Controls.DataGridCell)grid.Columns[2].GetCellContent(row).Parent;
                filterCell.RaiseEvent(Click(filterCell)); DoEvents();
                var combo = grid.Columns[2].GetCellContent(row) as System.Windows.Controls.ComboBox;
                bool opened = combo != null && combo.IsDropDownOpen;
                if (combo != null) { combo.SelectedIndex = 2; combo.IsDropDownOpen = false; }
                DoEvents();
                Check("the filter type is a combo box that opens on one click and writes the choice (" + probe.Filter + ")",
                      opened && probe.Filter == AnalogFilter.EmaOutlier && !filterCell.IsEditing);
            }
            finally { window.Close(); }
        }

        private void RunSecurityScenario(System.Text.StringBuilder sb)
        {
            var sec = _context.Security;
            var auth = sec.Auth;
            void Check(string what, bool ok) => sb.AppendLine((ok ? "PASS " : "FAIL ") + what);

            var probe = new System.Windows.Controls.Border();
            Access.SetRequires(probe, StationSecurity.Operate);
            Check("view-only without login", !sec.User.IsAuthenticated && !probe.IsEnabled);

            Check("wrong password refused", !Await(auth.LoginAsync("admin", "yanlis")).Success);
            Check("default admin logs in and must change the password",
                  Await(auth.LoginAsync("admin", TerakkiFramework.Security.UserStore.DefaultAdminPassword)).Success && auth.PasswordChangeRequired);
            Check("too short a new password refused", !Await(auth.ChangePasswordAsync("admin", "kisa")).Success);
            Check("password changed", Await(auth.ChangePasswordAsync("admin", "Sempa.2026")).Success && !auth.PasswordChangeRequired);
            Check("controls unlock after login", probe.IsEnabled);

            Check("admin adds an operator", Await(sec.Admin.AddAsync("op1", "Test Operatör", StationSecurity.Operator, "gecici123")) == null);
            Check("duplicate user name refused", Await(sec.Admin.AddAsync("OP1", "", StationSecurity.Operator, "gecici123")) != null);
            Check("last admin cannot be demoted", sec.Admin.SetLevel(sec.User.Account, StationSecurity.Operator) != null);
            Check("own account cannot be deleted", sec.Admin.Delete(sec.User.Account) != null);

            auth.Logout("Çıkış");
            Check("logout locks controls again", !probe.IsEnabled);

            Check("new operator must set a password", Await(auth.LoginAsync("op1", "gecici123")).Success && auth.PasswordChangeRequired);
            Await(auth.ChangePasswordAsync("gecici123", "operator.1"));
            Check("operator may operate but not edit the library or manage users",
                  sec.User.Has(StationSecurity.Operate) && !sec.User.Has(StationSecurity.EditLibrary) && !sec.User.Has(TerakkiFramework.Security.SecurityPermissions.ManageUsers));
            auth.Logout("Çıkış");

            for (int i = 0; i < sec.Auth.Options.MaxFailedAttempts; i++) Await(auth.LoginAsync("op1", "yanlis"));
            Check("wrong passwords lock the account, even against the right one",
                  !Await(auth.LoginAsync("op1", "operator.1")).Success && sec.Store.Find("op1").IsLocked);

            Check("admin unlocks it", Await(auth.LoginAsync("admin", "Sempa.2026")).Success
                                      && sec.Admin.Unlock(sec.Store.Find("op1")) == null && !sec.Store.Find("op1").IsLocked);

            var reloaded = new TerakkiFramework.Security.UserStore(sec.Store.Path, StationSecurity.Admin);
            reloaded.Load();
            Check("users.xml round-trips", reloaded.LoadError == null && reloaded.Accounts.Count == 2
                                          && TerakkiFramework.Security.PasswordHasher.Verify("operator.1", reloaded.Find("op1").PasswordHash));
            Check("user log recorded", sec.Log.Entries.Count >= 10);
        }

        /// <summary>Waits for an async call while keeping the dispatcher running, as its continuations need it.</summary>
        private T Await<T>(System.Threading.Tasks.Task<T> task)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, System.Threading.Tasks.TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            return task.Result;
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

        private void CaptureDefault(FrameworkElement view, string dir, string name, System.Text.StringBuilder sb)
        {
            try
            {
                var host = new Window
                {
                    Content = view, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = 0, Background = (System.Windows.Media.Brush)FindResource("B.App"),
                };
                host.Show();
                host.UpdateLayout();
                Steps(3);
                Capture(host, Path.Combine(dir, name + ".png"));
                host.Close();
                sb.AppendLine("OK   " + name);
            }
            catch (Exception ex)
            {
                sb.AppendLine("FAIL " + name + " : " + ex.GetType().Name + " — " + ex.Message);
            }
        }

        private static void Capture(Window shell, string path)
        {
            var root = shell.Content as FrameworkElement;
            if (root == null) return;
            int w = (int)root.ActualWidth, h = (int)root.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            // Drawn through a brush so an element centred in its window (a modal's card) lands at the origin
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w, h));
            rtb.Render(visual);
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
