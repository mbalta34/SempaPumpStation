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
        public const int Minor = 14;
        public const int Build = 1;

        public const string ProductName = "Sempa HMI";

        /// <summary>
        /// The MainPLC program this HMI is built for: FB_Station_Main's PLC_VERSION constant, written to
        /// "StationPh".PlcVersion (project Sempa_Pump_Station_V1.6.0, PLC_VERSION 1.6.0). Another version in the PLC raises an alarm.
        /// </summary>
        public const string ExpectedPlcVersion = "1.6.0";

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
            new ChangeLogEntry("1.14.1", "2026-11-23", new[]
            {
                "TV screen: only Q, Hm, Pp and efficiency, as four large tiles (2 × 2), with the header and the state banner; the smaller tiles (pressures, Hb, He, Nş, U, I, speed, torque, temperatures, vibration, sound) are gone.",
                "Zone screen: a COS φ tile next to Nş, the selected source's cos φ that Nş is computed with (on the 630 kW VFD the drive's motor side).",
                "Energy meters: the ATV660 card shows the motor side's cos φ, output kW / (√3·U·I), as the zones on the drive take it.",
            }),

            new ChangeLogEntry("1.14.0", "2026-11-23", new[]
            {
                "HMI parameters and command buttons with the entry counter, as in RailHMI / RailBase 1.9.2: every Hp parameter and button carries an EntryCounter (Byte) after its value, the HMI counts every entry (1 … 255, never 0; TerakkiFramework 169ad6b / cc7da23) and FB_Hmi_* act on a change of the counter, not of the value. An entry that repeats the value, or re-enters one the PLC refused, counts too; the HMI no longer writes the PLC's own value first. 0 only re-syncs (the HMI-to-PLC blocks after a PLC restart).",
                "FC_Station_LoadInstrument enters the library values like an HMI: a value that differs from the one last entered is written and its entry counted (FC_Hmi_NextEntryCount).",
                "Needs PLC program 1.6.0 (every Hp parameter grows by the EntryCounter; the download reinitialises the instance DBs with HMI parameters).",
            }),

            new ChangeLogEntry("1.13.0", "2026-11-23", new[]
            {
                "A zone on the 630 kW VFD takes U, I and cos φ from the ATV660's motor side instead of the source's PM5550, which sits on the drive's input: U = motor voltage, I = motor current, cos φ = output electrical power / (1.732 · U · I), so Nş is the drive's output power (FC_Station_DriveElectrical in FB_Station_Main; samples and reports alike). While the drive does not answer both requests, the meter's values are used.",
                "Excel report: column F holds the suction transmitter's value as measured (bar, negative under vacuum) instead of its inverse, and Hm (column I) is Hb − He: G × 10.2 − F × 10.2 + H + AG. The same Hm as before; the template is rebuilt with Make-ReportTemplate.ps1.",
                "TV screen in the application's theme, like the zone screen: the zone, its source and state, KAYIT NO and DENEY NO, Q, Hm, Pp and efficiency as large tiles and every other value of the zone (pressures, Hb, He, Nş, U, I, speed, torque, temperatures, vibration, sound) as smaller tiles; still scaled to any screen.",
                "TV outputs: each zone's TV picture full screen on a monitor chosen on the TV Ekranı page (one zone per monitor; Ekranları tanımla shows each monitor's number on it). The choice is remembered and the pictures come back on the same monitors when the HMI starts; they close with the HMI.",
                "Zone screen in the report's convention: He is the suction pressure as measured × 10.2 (negative under vacuum), Hm = Hb − He + velocity head + height.",
                "The Sempa logo in the window header, as the application icon and on the TV screens.",
                "Needs PLC program 1.5.1.",
            }),

            new ChangeLogEntry("1.12.0", "2026-11-22", new[]
            {
                "630 kW VFD: the ATV660 is read over Modbus TCP (read only; start, stop and speed reference stay on the wired IO): output frequency, motor current, motor voltage, motor torque (% of nominal and Nm), motor power (% of nominal and shaft power in kW), output electrical power in kW, mains voltage and the status word. Shown at the top of Enerji Analizörleri. PLC: DB_Communication_DriveDevices (IP, two connections), FB_Communication_Atv600Decoder, \"Drives\".Vfd630 (DB304).",
                "Energy meters and the ATV660: the filter offers none, EMA or EMA + outlier rejection (FB_Communication_ValueFilter, shared by both), then the moving average. The outlier jump is in % of the value (Maks. sıçrama %, default 20 %; Red adedi, default 5), since the values have different units. The drive has its own setting on its card (\"StationHp\".Drive630).",
                "Energy meter filters: the 100 ms sample pulse now really pulses (an S7-1500 IEC timer updates Q when it is read, so the old \"IN := NOT Q\" never let the filters take a value: EMA stayed at its first value, the average stayed off).",
                "Needs PLC program 1.5.0.",
            }),

            new ChangeLogEntry("1.11.0", "2026-11-22", new[]
            {
                "Analog inputs: optional moving average per channel (Ortalama), after the filter: the mean of the last n filtered values, one per OB30 cycle (100 ms); 0 or 1 = off, at most 100 = 10 s. For continuous pulsation such as a pump's suction pressure. Set on Analog Ayarları for the fixed sensors and in the instrument library for flowmeters, transmitters and vacuummeters.",
                "Instrument library: coefficient, offset and moving average (Katsayı, Ofset, Ortalama) per device, in a new DB StationInstrumentAdjustments (DB207, same device numbers; kept apart so the library's entries survive the download). The mounted device's values apply to its position at once; the instrument positions only show them.",
                "Energy meters: a filter per PM5550 (FB_Communication_PowermeterFilter), applied to every instantaneous value of that meter: none or EMA, then an optional moving average, a value every 100 ms. The energy counters are not filtered. Set on Enerji Analizörleri; the zones, samples and reports get the filtered values.",
                "Energy meters for a 3-wire system (PM5550 set to 3PH3W): no line-to-neutral values any more. The phase lines show the line-to-line voltages (L1-L2, L2-L3, L3-L1) and the phase currents; powers and cos φ are totals only.",
                "Retained analog input parameters that read 0 after a reinitialising download fall back to working defaults in the PLC (Max Ham 27648 when Min and Max are both 0, EMA α 0.2, maks. sıçrama 1000, red adedi 5, katsayı 1) instead of freezing or dividing by zero; the same for a meter's EMA α.",
                "Needs PLC program 1.4.9.",
            }),

            new ChangeLogEntry("1.10.0", "2026-11-22", new[]
            {
                "Analog inputs: signal type per channel, an input of FB_Io_AnalogInput (signalType, constants SIGNAL_4_20_MA, SIGNAL_0_20_MA, SIGNAL_0_10_V, SIGNAL_MINUS10_10_V; default 4…20 mA), passed by FB_Station_Instruments: torque −10…10 V, speed 0…10 V, the rest 4…20 mA. Not set on the HMI. MinRaw / MaxRaw are the raw values at the two ends of the signal range.",
                "Analog Ayarları and the instrument positions show the raw value, the filtered raw value, and both as the signal (mA or V), with the channel's signal type.",
                "Analog inputs: coefficient and offset per channel, Pv = scaled value × coefficient + offset (a coefficient of 0 is taken as 1). Set on Analog Ayarları and on the instrument positions.",
                "No more Yükle on the instrument positions: a mounted device's library entry applies to its channel at once, also while the zone runs. Instrument positions, flowmeter, discharge transmitter and vacuummeter can be changed while the pump runs; only the power source stays locked once start is given.",
                "Excel report and Deney Raporu: the torque meter's shaft power Nmil = T × n / 9549 kW (P [W] = T [Nm] × n [rpm] / 9.549, report column R) and the pump efficiency on it, hydraulic power / Nmil (column S). The report template is rebuilt with Make-ReportTemplate.ps1; the test number moved to AU1.",
                "Drive feedback watchdog: a drive commanded to run that does not report running for 2 s (at start or while running) raises a fault that stops the pump and drops the drive's run output, until the fault is reset.",
                "A data block that is missing or short counts as a PLC program mismatch (link stopped until restart) only when it stays so for 15 s: a download replaces the blocks, and a read in that moment no longer stops the link.",
                "Needs PLC program 1.4.7.",
            }),

            new ChangeLogEntry("1.9.2", "2026-11-22", new[]
            {
                "Nş (zone screen, report) is 1.732 × |cos φ| × U × I / 1000 with the magnitude of the meter's power factor, whose sign only tells the quadrant (leading / lagging): a negative power factor no longer zeroes Nş, Pp and the efficiency. The Excel report's cos φ column gets the same magnitude.",
                "Zone screen sample table: Hm uses the loaded recipes' pipe diameters (velocity head), as the live tiles and the report do.",
                "Combo boxes (filter type in the tables) in the dark theme: dark dropdown, items in the theme's text colour, the selected item highlighted. In the tables the filter combo fills its cell.",
                "Analog Ayarları and the instrument library show the PLC's Real values rounded to 4 decimal places; EMA α as x.xxxx (0.0200, not 0.0199999995).",
                "Needs PLC program 1.4.3.",
            }),

            new ChangeLogEntry("1.9.1", "2026-11-21", new[]
            {
                "The zone and TV screens calculate as the Excel report: Hm includes the velocity head difference between the discharge and suction pipes (10.2 m per bar); Pp is the pump power, √3·U·I·cosφ from the source's PM5550 times the motor efficiency at its load (motor recipe); the efficiency is the pump efficiency Q·Hm/367.2 / Pp. The zone screen also shows the discharge and suction heads Hb and He (m) and the electrical power Nş; the report screen shows Pp and the efficiency per sample, and archives are recalculated when loaded.",
                "The Excel report leaves suction out for a submersible pump, as the live head does.",
                "The filter type is chosen from a list (Yok, EMA, EMA + aykırı değer) in the instrument library and on Analog Ayarları; a value edited in a table is written as soon as its cell is left.",
                "The instrument positions show each channel's raw and filtered raw value next to its scaled value.",
                "The HMI reconnects to the PLC by itself: a link whose reads keep failing (cable pulled, PLC restarted) is closed and tried again, 3 attempts every 30 seconds (config.xml ReconnectAttempts, ReconnectIntervalSeconds) until the PLC answers. The header shows the link state and when the next attempt comes.",
                "Digital inputs can be forced like the outputs (IO İzleme, Dijital Girişler tab): Zorla 1 / Zorla 0 / Bırak and Tümünü Bırak, confirmed, engineer login; the program uses the forced value instead of the field, and every forced input is a warning in the alarm list.",
                "Needs PLC program 1.4.3.",
            }),

            new ChangeLogEntry("1.9.0", "2026-11-21", new[]
            {
                "IO-Link ports as in IO list V1.9: every instrument is on the zone's analog hub. Zones 2 and 3 (BNI00KM): pressure transmitters Pt1-Pt2 on ports 0-1, vacuummeters Vakum1-Vakum2 on 2-3, flowmeters FM1-FM4 on 4-7. Zone 1 (BNI0007): Pt1 on port 0, Vakum1 on 1, FM1-FM2 on 2-3. The BNI00AJ carries torque, speed, sound, water and ambient temperature (ports 0, 1, 2, 4, 5) in every zone. 20 instrument positions.",
                "The suction vacuummeter is chosen on the zone screen (Emme vakummetresi) like the discharge transmitter; the choice is stored with every sample and in the archive (column Vakummetre).",
                "New Analog Ayarları screen: range (min/max raw, min/max value) and filter settings of the fixed sensors (torque, speed, sound, temperatures, vibration), with their live raw, filtered and scaled values. Engineer login.",
                "The IO-Link screen shows the raw value the channel works with, also for a channel bound straight to its hardware word.",
                "The instrument screen has two tabs, Enstrüman Pozisyonları and Enstrüman Kütüphanesi, each full width with larger text.",
                "A click on a value field or an editable table cell selects the whole value, so typing replaces it; table cells edit with one click.",
                "Yükle on the instrument screen also works while the zone runs: it then takes over only the library entry's filter settings (type, alpha, max jump, reject count); the range stays as the test started. Changing the device stays locked while the zone runs.",
                "Needs PLC program 1.4.3.",
            }),

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
