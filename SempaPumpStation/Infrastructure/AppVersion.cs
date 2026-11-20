using System.Collections.Generic;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The single place the application's version lives (as in RailHMI). Bump <see cref="Major"/> /
    /// <see cref="Minor"/> / <see cref="Build"/> here and add a matching entry at the top of
    /// <see cref="ChangeLog"/>: everything that shows the version reads from this, so nothing
    /// else needs touching. A release is committed as "Sempa HMI V1.2.3"; a version delivered to the
    /// customer is also tagged "sempa-v1.2.3".
    /// </summary>
    public static class AppVersion
    {
        // ---- bump these ----
        public const int Major = 1;
        public const int Minor = 8;
        public const int Build = 0;

        public const string ProductName = "Sempa HMI";

        /// <summary>
        /// The MainPLC program this HMI is built for: FB_Station_Main's PLC_VERSION constant, written to
        /// "StationPh".PlcVersion (project Sempa_Pump_Station_V1.4.2). Another version in the PLC raises an alarm.
        /// </summary>
        public const string ExpectedPlcVersion = "1.4.2";

        /// <summary>Tooltip of the PLC version in the rail.</summary>
        public static string PlcVersionNote => "Bu HMI, PLC programı " + ExpectedPlcVersion + " için yapıldı.";

        /// <summary>e.g. "1.0.0"</summary>
        public static string Version => $"{Major}.{Minor}.{Build}";

        /// <summary>e.g. "Sempa HMI 1.0.0"</summary>
        public static string DisplayName => $"{ProductName} {Version}";

        /// <summary>
        /// Release notes, newest first. Add a new <see cref="ChangeLogEntry"/> at the top whenever
        /// the version is bumped; each line is one bullet shown in the version tooltip.
        /// </summary>
        public static readonly IReadOnlyList<ChangeLogEntry> ChangeLog = new[]
        {
            new ChangeLogEntry("1.8.0", "2026-11-19", new[]
            {
                "The PLC numbers the tests: a retained counter (StationCounters, DB206) gives each started test its number, so it no longer starts over when the HMI restarts. The zone, overview and TV screens show it.",
                "A contactor or magnetic switch that changes state without a command (closed or opened by hand, a tripped switch, a lost feedback) stops every motor of the station at once. The zone shows which device it was and raises an alarm; the motors can start again once the device is back in its commanded position and Arıza Reset is pressed in that zone. Until then no contactor or switch closes again by itself: what opened stays open. Opening by itself counts after 200 ms, so an E-stop (which drops the coils through its own circuit) is not taken for a manipulation.",
                "Button lamps (IO list V1.7): start lit while the pump runs, stop lit while it is stopped, take sample lit for a second after a press, both E-stops lit unless pressed. The panel E-stop is read and stops its zone like the pendant E-stop. The IO-Link screen shows the BNI007Z ports as wired in IO list V1.8.",
                "Recipes are saved as XML files. Recipes saved earlier as .txt are still listed and loaded, and become XML when saved again.",
                "IO-Link ports as in IO list V1.8, with the zone 1 and zone 3 layouts swapped as built: zones 2 and 3 have four flowmeters (FM1-FM4, BNI00KM ports 4-7), zone 1 two (FM1-FM2 on BNI00AJ ports 0-1, pressure transmitters on a BNI0007); every zone has a torque meter (torque) and a speed sensor on its BNI00AJ. The separate torque-meter speed channel is gone: the report's speed column (Q) and the test speed take the speed sensor.",
                "Needs PLC program 1.4.2.",
            }),

            new ChangeLogEntry("1.7.0", "2026-11-19", new[]
            {
                "Zone 1 has its own five flowmeters on ports 0-4 of its BNI00AJ hub, like zone 2; zones 1 and 2 no longer share flowmeters.",
                "Torque meter in zones 1 and 2 on ports 6 (speed) and 7 (torque) of the BNI00KM hub: shown on the zone and IO-Link screens, logged, and stored with every sample together with the supply's cos phi.",
                "The Testi Yapan recipe page is gone: the tester in the archive and the report is the user logged in when the test ends.",
                "Excel Raporu on the report screen fills the 2017 Sempa test report template (Excel) for the selected test, with two extra columns for torque and torque-meter speed.",
                "Needs PLC program 1.3.7.",
            }),

            new ChangeLogEntry("1.6.1", "2026-11-18", new[]
            {
                "Magnetic switches (560 kW, 630 kW): the charge-spring output stays on while the switch is open, because the spring-charged contact is powered through it; it used to drop once charged, which also dropped the charged signal.",
                "The simulation powers the spring-charged contact through the charge output, as on site.",
                "Needs PLC program 1.3.6.",
            }),

            new ChangeLogEntry("1.6.0", "2026-11-18", new[]
            {
                "Rotation test (Yön testi) on the zone screen: the pump runs for the set number of seconds and stops; the operator then confirms the direction (Yön doğru). Testi Başlat is enabled only after the confirmation.",
                "The confirmation is cleared when the zone's power source changes or is disconnected, and when a test ends.",
                "Needs PLC program 1.3.5 (rotation test in FB_Station_Zone, longer StationHp/StationPh).",
            }),

            new ChangeLogEntry("1.5.0", "2026-11-16", new[]
            {
                "New Enerji Analizörleri screen: the eight PM5550 meters, one per power source, with phase voltages, currents, powers and power factor, totals, frequency, energy and whether each meter answers.",
                "Pp on the TV and zone screens is the active power measured by the selected source's meter; efficiency is hydraulic power over that power. The zone screen also shows voltage and current.",
                "New TV Ekranı screen: Q, Hm, Pp and efficiency in large figures with the sample and test numbers, like the old TV page.",
                "Continuous data log of the zones' values to SQLite, CSV or XML (config.xml DataLog), during tests and on every sample.",
                "The PLC program version is shown under the HMI version in the navigation rail; a PLC running another version than the one this HMI is built for (PLC 1.3.4) raises a warning.",
                "Buttons pulse their Click and parameter entries always reach the PLC, also re-entering a value it refused before, for BluePrism's FB_Hmi_* blocks, which never write the HMI→PLC data blocks.",
                "Needs Powermeters (DB303, standard access), the Modbus TCP blocks, StationPh.PlcVersion and BluePrism's FB_Hmi_* (hp in / ph out on the entry blocks) in the PLC.",
            }),

            new ChangeLogEntry("1.4.0", "2026-11-04", new[]
            {
                "The IO screen has a fourth tab, Analog Çıkışlar: the speed reference sent to each VFD, with its raw value, range and forcing state.",
                "The version is shown in the navigation rail; hovering over it shows this release's notes.",
            }),

            new ChangeLogEntry("1.3.0", "2026-11-04", new[]
            {
                "New IO İzleme screen: the 82 digital and 41 analog inputs live, and the 53 digital outputs with Force ON / Force OFF / Release, as in BluePrism's maintenance HMI.",
                "Forcing an output needs an engineer login, is confirmed every time and shows a warning in the alarm list until the output is released. Tümünü Bırak releases every forced output.",
                "The HMI starts full screen, over the taskbar. The window button in the header, or F11, switches to a normal window and back.",
                "Needs MaintenanceHp (DB101) with standard access in the PLC for the force commands.",
            }),

            new ChangeLogEntry("1.2.0", "2026-11-04", new[]
            {
                "New IO-Link screen: each zone's BNI00L3 master and its hubs as in the IO list, with what every port carries and its live and raw value. On a flowmeter, pressure transmitter or vacuummeter port the connected library device is chosen.",
                "The HMI stops the PLC connection when the PLC runs another program version (a data block missing or shorter than this HMI expects), so nothing is written to the wrong addresses. An alarm and a red connection light show it until the HMI is restarted.",
                "Needs IoLink (DB205) in the PLC.",
            }),

            new ChangeLogEntry("1.1.0", "2026-11-03", new[]
            {
                "User login with the Terakki framework: Operatör, Mühendis and Yönetici levels, passwords that must be changed at the first login, lockout after five wrong passwords, automatic logout after 15 minutes without input.",
                "Without a login the HMI is view-only; stopping a pump is never locked.",
                "Kullanıcılar screen for administrators and a login history (Kullanıcı Kaydı).",
            }),

            new ChangeLogEntry("1.0.0", "2026-11-03", new[]
            {
                "First version for the three-zone MainPLC: overview, zone, instrument, report, trend, recipe and alarm screens.",
                "Runs against the real PLC over S7 or against a built-in simulation of the PLC program (config.xml).",
            }),
        };

        /// <summary>This release's notes, for the version tooltip.</summary>
        public static string CurrentNotes
        {
            get
            {
                if (ChangeLog == null || ChangeLog.Count == 0)
                    return DisplayName;

                var latest = ChangeLog[0];
                var sb = new System.Text.StringBuilder();
                sb.Append(ProductName).Append("  ").AppendLine(latest.Header);
                foreach (var c in latest.Changes)
                    sb.Append("  • ").AppendLine(c);
                return sb.ToString().TrimEnd();
            }
        }
    }

    public class ChangeLogEntry
    {
        public ChangeLogEntry(string version, string date, IReadOnlyList<string> changes)
        {
            Version = version;
            Date = date;
            Changes = changes;
        }

        public string Version { get; }
        public string Date { get; }
        public IReadOnlyList<string> Changes { get; }

        /// <summary>"1.0.0 — 2026-07-19" for the changelog header.</summary>
        public string Header => $"{Version}  —  {Date}";
    }
}
