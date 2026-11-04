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
        public const int Minor = 4;
        public const int Build = 0;

        public const string ProductName = "Sempa HMI";

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
