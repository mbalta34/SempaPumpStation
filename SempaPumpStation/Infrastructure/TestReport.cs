using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The Excel test report: a copy of Reports\Sempa_Rapor_Sablon.xlsx (the 2017 Sempa report with the torque
    /// meter's four extra columns: P torque and Q speed, filled here; R shaft power T × n / 9549 kW and S the pump
    /// efficiency on it, formulas) filled with an archived test. Only the input cells
    /// are written; the template's formulas (head, power, efficiency, NPSH) and charts do the rest, and
    /// Excel recalculates them when the file is opened. Excel is not needed to create the file.
    /// </summary>
    public static class TestReport
    {
        public const string SheetName = "Sablon";
        public const int FirstRow = 32;
        /// <summary>Rows 32–51 of the template.</summary>
        public const int MaxSamples = 20;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string TemplatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports", "Sempa_Rapor_Sablon.xlsx");

        /// <summary>Writes the report next to the test's CSV (same name, .xlsx) and returns its path.</summary>
        public static string Create(TestRecord r, string target = null)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (!File.Exists(TemplatePath)) throw new FileNotFoundException("Rapor şablonu bulunamadı: " + TemplatePath, TemplatePath);

            if (target == null)
            {
                string csv = r.File ?? Path.Combine(TestArchive.Folder, "Deney_" + r.TestNo + "_Bolge" + r.Zone + ".csv");
                target = Path.ChangeExtension(csv, ".xlsx");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(TemplatePath, target, true);
            File.SetAttributes(target, FileAttributes.Normal);

            using (var zip = ZipFile.Open(target, ZipArchiveMode.Update))
            {
                string sheetPath = SheetPath(zip, SheetName);
                var sheet = Load(zip, sheetPath);
                Fill(new SheetWriter(sheet), r);
                Save(zip, sheetPath, sheet);

                int rows = Math.Max(1, Math.Min(MaxSamples, r.Samples.Count));
                LimitCharts(zip, FirstRow + rows - 1);
                RecalculateOnOpen(zip);
            }
            return target;
        }

        // ---------------------------------------------------------------- content
        private static void Fill(SheetWriter w, TestRecord r)
        {
            var h = r.Header;

            // Title row and customer
            w.Number("N1", ParseTime(r.Ended ?? r.Started).ToOADate());
            w.Number("AU1", r.TestNo);   // next to "DENEY NO :" (AT1)
            w.Text("D3", h["WRT_FIRMA_ADI"]);
            w.Text("J3", h["WRT_ADRES"]);

            // Motor
            w.Text("D5", h["WRT_MOTOR_MARKA"]);
            w.Text("F5", h["WRT_MOTOR_TIPI"]);
            w.Text("D6", h["WRT_MOTOR_FRAME"]);
            w.Value("D8", h["WRT_MOTOR_VERIM_4_4"]);   // load 100 %
            w.Value("E8", h["WRT_MOTOR_VERIM_3_4"]);   // 75 %
            w.Value("F8", h["WRT_MOTOR_VERIM_2_4"]);   // 50 %
            w.Value("G8", h["WRT_MOTOR_VERIM_1_4"]);   // 25 %
            w.Text("D9", h["WRT_MOTOR_VERIM_SIN"]);
            w.Value("D10", h["WRT_MOTOR_GUCU"]);
            w.Value("D11", h["WRT_MOTOR_DEVRI"]);
            w.Value("D12", h["WRT_MOTOR_HP"]);
            w.Value("D13", h["WRT_MOTOR_COSPHI"]);
            w.Value("D14", h["WRT_MOTOR_AKIMI"]);
            w.Text("D15", h["WRT_MOTOR_SERI_NO"]);
            double testSpeed = TestSpeed(r);
            if (testSpeed > 0) w.Number("F11", Math.Round(testSpeed));

            // Test stand
            w.Number("M5", r.Zone);
            w.Text("M6", Instrument(h["DEBIMETRE"]));
            w.Text("M7", Instrument(h["BASINC_TR"]));
            w.Text("M8", Instrument(h["VAKUMMETRE"]));
            double suctionDn = PumpCalc.FirstNumber(h["WRT_PMP_EMME_CAP"]), dischargeDn = PumpCalc.FirstNumber(h["WRT_PMP_BAS_CAP"]);
            double dischargeLine = PumpCalc.FirstNumber(h["HAT_BAS_IC_CAPI"]);
            if (suctionDn > 0) w.Number("M13", suctionDn);
            if (dischargeLine > 0) w.Number("M14", dischargeLine);
            else if (dischargeDn > 0) w.Number("M14", dischargeDn);
            w.Value("M15", h["GEO_YUKSEKLIK"]);
            // Mechanical efficiency (AB = M11): a factor of Np (column N) only; the torque meter's efficiency (S) divides by the
            // measured shaft power R, as the template has it
            w.Number("M11", PumpCalc.EfficiencyFactor(h["WRT_PMP_MEKANIK_VERIM"]));

            // Pump
            w.Text("D18", h["WRT_POMPA_TIPI"]);
            w.Text("D19", h["WRT_PMP_SERI_NO"]);
            if (suctionDn > 0) w.Number("D20", suctionDn); else w.Text("D20", h["WRT_PMP_EMME_CAP"]);
            if (dischargeDn > 0) w.Number("D21", dischargeDn); else w.Text("D21", h["WRT_PMP_BAS_CAP"]);
            w.Text("D22", h["WRT_SALMAST_TIPI"]);
            w.Text("L18", h["WRT_SALYANGOZ_MALZ"]);
            w.Text("L19", h["WRT_PMP_MIL_MALZ"]);
            w.Text("L20", h["WRT_PMP_MONTAJ_TIP"]);
            w.Text("L21", h["WRT_CARK_MALZ"]);
            double impeller = PumpCalc.FirstNumber(h["WRT_CARK_CAPI"]);
            if (impeller > 0) w.Number("L22", impeller); else w.Text("L22", h["WRT_CARK_CAPI"]);

            // Measurements: Q, the suction and discharge pressures as the transmitters read them (bar; suction negative under
            // vacuum), cos φ, V, I, torque and speed. The template's Hm (column I) is Hb − He: G × 10.2 − F × 10.2 + H + AG.
            // A submersible pump has no suction gauge: F stays 0, as the live head (PumpCalc) leaves suction out
            bool submersible = PumpLine.From(k => h[k]).Submersible;
            for (int i = 0; i < Math.Min(MaxSamples, r.Samples.Count); i++)
            {
                var s = r.Samples[i];
                int row = FirstRow + i;
                w.Number("D" + row, s.Flow);
                w.Number("F" + row, submersible ? 0.0 : s.SuctionPressure);
                w.Number("G" + row, s.DischargePressure);
                double cos = Math.Abs(s.PowerFactor);   // the meter's sign is the quadrant; the report wants cos φ
                if (cos > 0 && cos <= 1) w.Number("J" + row, cos);
                w.Number("K" + row, s.Voltage);
                w.Number("L" + row, s.Current);
                w.Number("P" + row, s.Torque);
                w.Number("Q" + row, s.Speed);
            }

            w.Text("A53", h["TESTI_YAPAN"]);
        }

        /// <summary>Mean speed over the samples the pump ran in.</summary>
        private static double TestSpeed(TestRecord r)
        {
            var running = r.Samples.Where(s => s.PumpRunning).ToList();
            if (running.Count == 0) return 0;
            return running.Average(s => s.Speed);
        }

        /// <summary>"9 · Krohne Optiflux DN80" → "Krohne Optiflux DN80"; "—" → empty.</summary>
        private static string Instrument(string described)
        {
            if (string.IsNullOrWhiteSpace(described) || described.Trim() == "—") return "";
            return Regex.Replace(described, @"^\s*\d+\s*·\s*", "");
        }
        private static DateTime ParseTime(string text) =>
            DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out DateTime t) ? t : DateTime.Now;

        // ---------------------------------------------------------------- package parts
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

        private static string SheetPath(ZipArchive zip, string name)
        {
            var wb = Load(zip, "xl/workbook.xml");
            var sheet = wb.Root.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault(e => (string)e.Attribute("name") == name)
                        ?? throw new InvalidDataException("Şablonda '" + name + "' sayfası yok.");
            string id = (string)sheet.Attribute(RelNs + "id");
            var rels = Load(zip, "xl/_rels/workbook.xml.rels");
            string target = rels.Root.Elements(PkgRel + "Relationship").First(e => (string)e.Attribute("Id") == id).Attribute("Target").Value;
            return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
        }

        /// <summary>The charts plot rows 32–51; shorten their ranges to the rows that hold samples.</summary>
        private static void LimitCharts(ZipArchive zip, int lastRow)
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/charts/chart") && e.FullName.EndsWith(".xml")).ToList())
            {
                string xml;
                using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();
                string changed = Regex.Replace(xml, @"(\$[A-Z]{1,3}\$" + FirstRow + @":\$[A-Z]{1,3}\$)" + (FirstRow + MaxSamples - 1) + @"\b",
                                               m => m.Groups[1].Value + lastRow);
                if (changed != xml) WriteText(zip, entry.FullName, changed);
            }
        }

        /// <summary>Full recalculation when Excel opens the file; the calculation chain is dropped so Excel rebuilds it.</summary>
        private static void RecalculateOnOpen(ZipArchive zip)
        {
            var wb = Load(zip, "xl/workbook.xml");
            var calc = wb.Root.Element(Main + "calcPr");
            if (calc == null)
            {
                calc = new XElement(Main + "calcPr");
                var after = wb.Root.Element(Main + "definedNames") ?? wb.Root.Element(Main + "sheets");
                after.AddAfterSelf(calc);
            }
            calc.SetAttributeValue("fullCalcOnLoad", "1");
            Save(zip, "xl/workbook.xml", wb);

            zip.GetEntry("xl/calcChain.xml")?.Delete();
            var rels = Load(zip, "xl/_rels/workbook.xml.rels");
            rels.Root.Elements(PkgRel + "Relationship").Where(e => ((string)e.Attribute("Type") ?? "").EndsWith("/calcChain")).Remove();
            Save(zip, "xl/_rels/workbook.xml.rels", rels);
            var types = Load(zip, "[Content_Types].xml");
            types.Root.Elements(ContentTypes + "Override").Where(e => (string)e.Attribute("PartName") == "/xl/calcChain.xml").Remove();
            Save(zip, "[Content_Types].xml", types);
        }

        private static XDocument Load(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Şablonda " + path + " yok.");
            using (var s = entry.Open()) return XDocument.Load(s);
        }

        private static void Save(ZipArchive zip, string path, XDocument doc)
        {
            zip.GetEntry(path)?.Delete();
            using (var s = zip.CreateEntry(path).Open()) doc.Save(s, SaveOptions.DisableFormatting);
        }

        private static void WriteText(ZipArchive zip, string path, string text)
        {
            zip.GetEntry(path)?.Delete();
            using (var w = new StreamWriter(zip.CreateEntry(path).Open(), new System.Text.UTF8Encoding(false))) w.Write(text);
        }

        // ---------------------------------------------------------------- cells
        /// <summary>Writes values into a worksheet's sheetData, keeping each cell's style.</summary>
        private sealed class SheetWriter
        {
            private readonly XElement _data;

            public SheetWriter(XDocument sheet)
            {
                _data = sheet.Root.Element(Main + "sheetData") ?? throw new InvalidDataException("sheetData yok.");
            }

            public void Number(string address, double value)
            {
                var c = Cell(address);
                Clear(c);
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                c.Add(new XElement(Main + "v", value.ToString("R", Inv)));
            }

            public void Text(string address, string text)
            {
                var c = Cell(address);
                Clear(c);
                if (string.IsNullOrEmpty(text)) return;
                c.SetAttributeValue("t", "inlineStr");
                c.Add(new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            }

            /// <summary>A number when the text is one (the archive stores numbers invariant), otherwise the text.</summary>
            public void Value(string address, string text)
            {
                if (double.TryParse(text, NumberStyles.Float, Inv, out double v)) Number(address, v);
                else Text(address, text);
            }

            private static void Clear(XElement c)
            {
                c.RemoveNodes();
                c.SetAttributeValue("t", null);
            }

            private XElement Cell(string address)
            {
                var m = Regex.Match(address, @"^([A-Z]+)(\d+)$");
                int col = ColumnIndex(m.Groups[1].Value), row = int.Parse(m.Groups[2].Value, Inv);

                var rowEl = _data.Elements(Main + "row").FirstOrDefault(e => (int)e.Attribute("r") == row);
                if (rowEl == null)
                {
                    rowEl = new XElement(Main + "row", new XAttribute("r", row));
                    var before = _data.Elements(Main + "row").LastOrDefault(e => (int)e.Attribute("r") < row);
                    if (before == null) _data.AddFirst(rowEl); else before.AddAfterSelf(rowEl);
                }

                var cell = rowEl.Elements(Main + "c").FirstOrDefault(e => (string)e.Attribute("r") == address);
                if (cell == null)
                {
                    cell = new XElement(Main + "c", new XAttribute("r", address));
                    var before = rowEl.Elements(Main + "c").LastOrDefault(e => ColumnIndex(Regex.Match((string)e.Attribute("r"), "^[A-Z]+").Value) < col);
                    if (before == null) rowEl.AddFirst(cell); else before.AddAfterSelf(cell);
                }
                return cell;
            }

            private static int ColumnIndex(string letters)
            {
                int n = 0;
                foreach (char ch in letters) n = n * 26 + (ch - 'A' + 1);
                return n;
            }
        }
    }
}
