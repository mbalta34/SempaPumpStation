using System;
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
                MessageBox.Show(ex.ToString(), "Başlatma hatası");
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
            foreach (var (tab, file) in new[] { (Screens.IoViewModel.AnalogInputsTab, "Io_Analog"), (Screens.IoViewModel.DigitalOutputsTab, "Io_Outputs") })
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

            // The login modal blocks until closed: capture it from inside, then cancel it
            vm.NavigateCommand.Execute(vm.MainNav[1]);
            bool shown = false;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var login = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<LoginWindow>(Windows));
                if (login == null) return;
                shown = true;
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
            c.SetValue(Station.PositionHp("Zone3_Pt2") + "Device.Value", (short)0);
            Steps(3);
            Check("transmitters read their fixed ports", Raw("Zone3_Pt1") == Hub(0) && Raw("Zone3_Pt2") == Hub(1)
                                                         && Hub(1) == IoLinkLayout.OpenInputRaw && Hub(0) != Hub(1));
            Check("fixed sensors read their IO list ports", Raw("Zone3_Speed") == c.GetInt(IoLinkLayout.Raw(3, IoLinkModule.RtdHub, 3)));

            // The device on a port is chosen on the IO-Link screen
            var screen = (Screens.IoLinkViewModel)System.Linq.Enumerable.First(vm.MainNav, e => e.Key == "IoLink").Screen;
            screen.Zones[2].Command.Execute(null);
            screen.Hubs[1].Ports[1].SelectCommand.Execute(null);   // BNI0007 port 1 = Pt2
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
            c.SetValue(Station.PositionHp("Zone3_Pt2") + "Device.Value", (short)pt2Device);
            Steps(3);
            Check("restored", c.GetInt(Ph("Zone3_Pt2") + "Device.Value") == pt2Device);

            screen.Zones[0].Command.Execute(null);
            screen.Hubs[1].Ports[0].SelectCommand.Execute(null);   // zone 1 Pt1, zone 1 runs
            Steps(2);
            Check("the device is locked while the zone runs", !screen.CanAssign);
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

            row.ForceOffCommand.Execute(null);
            Steps(3);
            Check("Force OFF holds the output at 0", row.Forcing && !row.Pv);

            screen.ReleaseAllCommand.Execute(null);
            Steps(10);
            Check("release returns the output to the program", !row.Forcing && !row.Pv && screen.ForcedCount == 0 && !c.Plc.Field.DeviceClosed(3, 1));

            // The contactor moved without a request: clear the zone's device fault again
            c.Click(Station.ZoneHp(3) + "ResetFault");
            Steps(20);

            // Input watch tables
            var io = (Screens.IoViewModel)System.Linq.Enumerable.First(vm.DiagNav, e => e.Key == "Io").Screen;
            io.ShowTab(Screens.IoViewModel.DigitalInputsTab);
            Steps(2);
            var inputs = System.Linq.Enumerable.ToList(System.Linq.Enumerable.SelectMany(io.InputGroups, g => g.Rows));
            Check("DI watch shows the field (zone 3 560 kW switch closed, not open)",
                  inputs.Count == 82 && inputs.Find(r => r.Name == "Zone3_Q560Ss_Closed").Pv && !inputs.Find(r => r.Name == "Zone3_Q560Ss_Open").Pv);
            io.ShowTab(Screens.IoViewModel.AnalogInputsTab);
            Steps(2);
            var pt2 = System.Linq.Enumerable.First(System.Linq.Enumerable.SelectMany(io.AnalogGroups, g => g.Rows), r => r.Name == "Zone1_Pt2");
            Check("AI watch shows value, raw and range (" + pt2.Value + ", " + pt2.Raw + ", " + pt2.Range + ")",
                  pt2.Value.EndsWith("bar") && pt2.Raw.Length > 0 && pt2.Range.Contains("16"));
            io.ShowTab(Screens.IoViewModel.DigitalInputsTab);
        }

        // ---------------------------------------------------------------- login system
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
