using System.Collections.Generic;
using TerakkiFramework.Plc;
using TerakkiFramework.Tags;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The tag catalogue of the station HMI on the in-memory Local backend. Tag names are the PLC
    /// symbol paths of MainPLC (StationHp / StationPh / StationSamples / StationInstruments /
    /// MaintenanceHp / MaintenancePh), so a later switch to the real PLC is a change of backend, not
    /// of screens. "Sim.*" tags are the simulated field (pendant buttons, E-stops, valves, faults) and
    /// do not exist in the PLC; the recipe / report tags are HMI-local, as in the original system.
    /// A DTL is carried as its members (Year, Month, … Nanosecond), like the S7 model.
    /// </summary>
    public static class TagCatalog
    {
        public static TagProject Build(bool simulatePlc)
        {
            var p = new TagProject { Name = "SEMPA Pompa Test İstasyonu", Protocol = ProtocolKind.Local };
            var t = p.Tags;

            // Against the real PLC the PLC data blocks come from the generated S7 models (PlcInterface);
            // only the HMI-local and simulated-field tags live on the Local backend then.
            if (simulatePlc)
            {
                for (int z = 1; z <= Station.ZoneCount; z++) AddZone(t, z);
                foreach (var pos in Station.Positions) AddPosition(t, pos.Name);
                foreach (var ch in Station.AnalogInputs()) AddAnalogInput(t, ch);
                for (int c = Station.Flowmeter; c <= Station.Vacuummeter; c++)
                    for (int d = 1; d <= Station.LibrarySize; d++)
                        foreach (var f in Station.InstrumentFields) Add(t, Station.LibraryEntry(c, d) + f.Name, f.Kind);
                for (int z = 1; z <= Station.ZoneCount; z++) AddSamples(t, z);
                for (int z = 1; z <= Station.ZoneCount; z++) AddIoLink(t, z);
            }
            AddSimulation(t);
            AddHmiLocal(t);
            return p;
        }

        private static void AddZone(List<TagDefinition> t, int z)
        {
            string hp = Station.ZoneHp(z), ph = Station.ZonePh(z);
            foreach (var n in Station.ZoneIntParameters) { I(t, hp + n + ".Value"); I(t, ph + n + ".Value"); B(t, ph + n + ".Enabled"); }
            R(t, hp + "SpeedReference.Value"); R(t, ph + "SpeedReference.Value"); B(t, ph + "SpeedReference.Enabled");
            foreach (var n in Station.ZoneButtons) { B(t, hp + n + ".Click"); B(t, ph + n + ".Value"); B(t, ph + n + ".Enabled"); }

            I(t, ph + "PowerState");
            foreach (var n in new[] { "PumpRunning", "DeviceFault", "DriveFault", "EStop", "SelectionRejected", "TestActive", "SampleBufferFull", "LampGreen", "LampYellow", "LampRed" })
                B(t, ph + n);
            I(t, ph + "SampleCount");
            foreach (var n in new[] { "Flow", "DischargePressure", "SuctionPressure" }) R(t, ph + n);
            foreach (var n in new[] { "FlowmeterDevice", "DischargeTransmitterDevice", "VacuummeterDevice" }) I(t, ph + n);
        }

        private static void AddPosition(List<TagDefinition> t, string pos)
        {
            string hp = Station.PositionHp(pos), ph = Station.PositionPh(pos);
            I(t, hp + "Device.Value"); B(t, hp + "Load.Click");
            I(t, ph + "Device.Value"); B(t, ph + "Device.Enabled");
            B(t, ph + "Load.Value"); B(t, ph + "Load.Enabled");
            B(t, ph + "SelectionRejected");
        }

        private static void AddIoLink(List<TagDefinition> t, int z)
        {
            foreach (var hub in new[] { IoLinkModule.RtdHub, IoLinkModule.AnalogHub })
                for (int p = 0; p < IoLinkLayout.PortCount; p++) I(t, IoLinkLayout.Raw(z, hub, p));
            I(t, IoLinkLayout.Field(z, "Vibration"));
            I(t, IoLinkLayout.Field(z, "Temperature"));
        }

        private static void AddAnalogInput(List<TagDefinition> t, string ch)
        {
            string hp = Station.AiHp(ch), ph = Station.AiPh(ch);
            foreach (var n in Station.AiRealParameters) { R(t, hp + n + ".Value"); R(t, ph + n + ".Value"); B(t, ph + n + ".Enabled"); }
            foreach (var n in Station.AiIntParameters) { I(t, hp + n + ".Value"); I(t, ph + n + ".Value"); B(t, ph + n + ".Enabled"); }
            R(t, ph + "Pv"); I(t, ph + "RawValue"); R(t, ph + "FilteredRaw"); B(t, ph + "WireBreak");
        }

        private static void AddSamples(List<TagDefinition> t, int z)
        {
            string s = Station.Samples(z);
            B(t, s + "TestActive"); Dtl(t, s + "TestStarted"); Dtl(t, s + "TestEnded"); I(t, s + "Count");
            for (int i = 1; i <= Station.SampleCapacity; i++)
                foreach (var f in Station.SampleFields) Add(t, Station.Sample(z, i) + f.Name, f.Kind);
        }

        private static void AddSimulation(List<TagDefinition> t)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string s = Station.SimZone(z);
                M(t, s + "PendantStart"); M(t, s + "PendantStop"); M(t, s + "PendantTakeSample");
                B(t, s + "EStopPressed"); B(t, s + "FeedbackFault");
                R(t, s + "ValveOpening");
            }
            for (int d = 1; d <= Station.SourceCount; d++)
            {
                B(t, Station.SimDrive(d) + "Fault");
                B(t, Station.SimDrive(d) + "Running");
            }
        }

        private static void AddHmiLocal(List<TagDefinition> t)
        {
            S(t, "TARIH_SAAT"); B(t, "PLC_COMM_OK"); B(t, "PLC_INTERFACE_OK"); I(t, "GLOBAL_DENEY_NO");
            B(t, "DALGIC_PMP_SEL");

            // ---- Report values (WRT_* = master data selected from the recipes) ----
            S(t, "WRT_FIRMA_ADI"); S(t, "WRT_ADRES"); S(t, "WRT_TESTI_YAPAN");
            S(t, "WRT_POMPA_TIPI"); S(t, "WRT_PMP_SERI_NO"); S(t, "WRT_PMP_EMME_CAP");
            S(t, "WRT_PMP_BAS_CAP"); S(t, "WRT_PMP_MIL_MALZ"); S(t, "WRT_PMP_MONTAJ_TIP");
            S(t, "WRT_CARK_MALZ"); S(t, "WRT_CARK_CAPI"); S(t, "WRT_SALMAST_TIPI"); S(t, "WRT_SALYANGOZ_MALZ");
            S(t, "WRT_MOTOR_MARKA"); S(t, "WRT_MOTOR_SERI_NO"); S(t, "WRT_MOTOR_FRAME");
            S(t, "WRT_MOTOR_HP"); S(t, "WRT_MOTOR_TIPI"); S(t, "WRT_MOTOR_VERIM_SIN");
            R(t, "WRT_MOTOR_GUCU"); R(t, "WRT_MOTOR_DEVRI"); R(t, "WRT_MOTOR_AKIMI"); R(t, "WRT_MOTOR_COSPHI");
            R(t, "WRT_MOTOR_VERIM_1_4"); R(t, "WRT_MOTOR_VERIM_2_4"); R(t, "WRT_MOTOR_VERIM_3_4"); R(t, "WRT_MOTOR_VERIM_4_4");

            // ---- Firma recipe edit buffers ----
            S(t, "FIRMA_ADI"); S(t, "YETKILI_KISI"); S(t, "ADRES"); S(t, "TEL_NO");
            S(t, "CEP_NO"); S(t, "FAX_NO"); S(t, "VERGI_DAIRESI"); S(t, "VERGI_NO");

            // ---- Pompa recipe edit buffers ----
            S(t, "POMPA_TIPI"); S(t, "PMP_SERI_NO"); S(t, "PMP_EMME_CAP"); S(t, "PMP_BAS_CAP");
            S(t, "PMP_MIL_MALZ"); S(t, "PMP_MONTAJ_TIP"); S(t, "PMP_SASE_DRM");
            S(t, "CARK_MALZ"); S(t, "CARK_CAPI"); S(t, "SALMAST_TIPI"); S(t, "SALYANGOZ_MALZ");

            // ---- Motor recipe edit buffers ----
            S(t, "MOTOR_TIPI"); S(t, "MOTOR_MARKA"); S(t, "MOTOR_SERI_NO"); S(t, "MOTOR_FRAME");
            S(t, "MOTOR_HP"); S(t, "MOTOR_VERIM_SIN");
            R(t, "MOTOR_GUCU"); R(t, "MOTOR_DEVRI"); R(t, "MOTOR_AKIMI"); R(t, "MOTOR_COSPHI");
            R(t, "MOTOR_VERIM_1_4"); R(t, "MOTOR_VERIM_2_4"); R(t, "MOTOR_VERIM_3_4"); R(t, "MOTOR_VERIM_4_4");

            // ---- Hat (test line geometry) recipe; the instruments themselves come from the PLC library ----
            R(t, "HAT_BAS_IC_CAPI"); R(t, "HAT_UZUNLUGU"); R(t, "GEO_YUKSEKLIK");
            I(t, "BASMA_DIRSEK_ADET"); I(t, "EMME_RED_ADET");
            R(t, "FDUZ_DEGERI"); R(t, "FDIRS_DEGERI"); R(t, "FRED_DEGERI");

            // ---- Testi Yapan recipe ----
            S(t, "TESTI_YAPAN");
        }

        /// <summary>HMI-local starting values: the selected report master data and the line geometry.</summary>
        public static Dictionary<string, object> InitialValues()
        {
            return new Dictionary<string, object>
            {
                { "PLC_COMM_OK", true },
                { "PLC_INTERFACE_OK", true },
                { "GLOBAL_DENEY_NO", 1042 },

                { "WRT_FIRMA_ADI", "AKDENIZ SU ARITMA A.Ş." },
                { "WRT_ADRES", "OSB 12. Cadde No:34, Konya" },
                { "WRT_TESTI_YAPAN", "İsmet Y." },
                { "WRT_POMPA_TIPI", "NM 65/250 A" },
                { "WRT_PMP_SERI_NO", "P-2024-8817" },
                { "WRT_PMP_EMME_CAP", "DN200" },
                { "WRT_PMP_BAS_CAP", "DN150" },
                { "WRT_PMP_MIL_MALZ", "AISI 420" },
                { "WRT_PMP_MONTAJ_TIP", "Yatay" },
                { "WRT_CARK_MALZ", "GG25 Font" },
                { "WRT_CARK_CAPI", "252 mm" },
                { "WRT_SALMAST_TIPI", "Mekanik" },
                { "WRT_SALYANGOZ_MALZ", "GG25" },
                { "WRT_MOTOR_MARKA", "Volt Motor" },
                { "WRT_MOTOR_SERI_NO", "M-77120" },
                { "WRT_MOTOR_FRAME", "225 M" },
                { "WRT_MOTOR_HP", "60 HP" },
                { "WRT_MOTOR_TIPI", "3~ Asenkron" },
                { "WRT_MOTOR_VERIM_SIN", "IE3" },
                { "WRT_MOTOR_GUCU", 45.0f },
                { "WRT_MOTOR_DEVRI", 2960.0f },
                { "WRT_MOTOR_AKIMI", 82.0f },
                { "WRT_MOTOR_COSPHI", 0.87f },
                { "WRT_MOTOR_VERIM_1_4", 88.5f }, { "WRT_MOTOR_VERIM_2_4", 92.1f },
                { "WRT_MOTOR_VERIM_3_4", 93.4f }, { "WRT_MOTOR_VERIM_4_4", 93.0f },

                { "HAT_BAS_IC_CAPI", 150.0f }, { "HAT_UZUNLUGU", 6.0f }, { "GEO_YUKSEKLIK", 0.6f },
                { "BASMA_DIRSEK_ADET", 2 }, { "EMME_RED_ADET", 1 },
                { "FDUZ_DEGERI", 0.02f }, { "FDIRS_DEGERI", 0.3f }, { "FRED_DEGERI", 0.2f },
                { "TESTI_YAPAN", "İsmet Y." },
            };
        }

        private static void Add(List<TagDefinition> t, string n, char kind)
        {
            switch (kind)
            {
                case 'R': R(t, n); break;
                case 'I': I(t, n); break;
                case 'B': B(t, n); break;
                case 'D': Dtl(t, n); break;
                default: S(t, n); break;
            }
        }

        /// <summary>A DTL, as its members (the same leaves the S7 model has).</summary>
        private static void Dtl(List<TagDefinition> t, string n)
        {
            foreach (var m in new[] { "Year", "Month", "Day", "Weekday", "Hour", "Minute", "Second", "Nanosecond" })
                t.Add(new TagDefinition { Name = n + "." + m, Address = n + "." + m, DataType = DataType.DINT });
        }

        private static void R(List<TagDefinition> t, string n) => t.Add(new TagDefinition { Name = n, Address = n, DataType = DataType.REAL });
        private static void I(List<TagDefinition> t, string n) => t.Add(new TagDefinition { Name = n, Address = n, DataType = DataType.INT });
        private static void B(List<TagDefinition> t, string n) => t.Add(new TagDefinition { Name = n, Address = n, DataType = DataType.BOOL });
        private static void M(List<TagDefinition> t, string n) => t.Add(new TagDefinition { Name = n, Address = n, DataType = DataType.MOMENTARY_BOOL });
        private static void S(List<TagDefinition> t, string n) => t.Add(new TagDefinition { Name = n, Address = n, DataType = DataType.STRING });
    }
}
