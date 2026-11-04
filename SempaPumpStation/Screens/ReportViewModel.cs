using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// The archived tests (one per Start Test … End Test) with their report header, samples and Q–H curve.
    /// The recipe buttons choose the master data that the next archived test is stamped with.
    /// </summary>
    public sealed class ReportViewModel : ScreenBase
    {
        public const double ChartWidth = 520, ChartHeight = 220;

        public override string Key => "Report";
        public override string Title => "Pompa Deney Raporu";

        public ObservableCollection<TestRecord> Records => Ctx.Archive.Records;

        private TestRecord _selected;
        public TestRecord Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                BuildChart();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => _selected != null;

        public PointCollection HeadCurve { get; private set; } = new PointCollection();
        public PointCollection HeadPoints { get; private set; } = new PointCollection();
        public string FlowAxisMax { get; private set; } = "";
        public string HeadAxisMax { get; private set; } = "";

        public ICommand SelectFirmaCommand { get; }
        public ICommand SelectPompaCommand { get; }
        public ICommand SelectMotorCommand { get; }
        public ICommand SelectHatCommand { get; }
        public ICommand SelectTestiYapanCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public ReportViewModel(HmiContext ctx) : base(ctx)
        {
            SelectFirmaCommand = new SyncCommand(() => Ctx.Navigate("FirmaRecipe"));
            SelectPompaCommand = new SyncCommand(() => Ctx.Navigate("PompaRecipe"));
            SelectMotorCommand = new SyncCommand(() => Ctx.Navigate("MotorRecipe"));
            SelectHatCommand = new SyncCommand(() => Ctx.Navigate("HatRecipe"));
            SelectTestiYapanCommand = new SyncCommand(() => Ctx.Navigate("TestiYapan"));
            OpenFileCommand = new SyncCommand(() => Open(_selected?.File));
            OpenFolderCommand = new SyncCommand(() => { Directory.CreateDirectory(TestArchive.Folder); Open(TestArchive.Folder); });

            ctx.Archive.Archived += r => Selected = r;
            Selected = Records.FirstOrDefault();
        }

        private static void Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Dosya açılamadı"); }
        }

        /// <summary>Q–H points scaled into the chart box; the curve joins them in flow order.</summary>
        private void BuildChart()
        {
            var samples = _selected?.Samples.Where(s => s.Flow >= 0).OrderBy(s => s.Flow).ToList();
            var curve = new PointCollection();
            var points = new PointCollection();
            if (samples != null && samples.Count > 0)
            {
                double qMax = NiceMax(samples.Max(s => s.Flow)), hMax = NiceMax(samples.Max(s => s.Head));
                foreach (var s in samples)
                {
                    var p = new Point(s.Flow / qMax * ChartWidth, ChartHeight - Math.Max(0, s.Head) / hMax * ChartHeight);
                    curve.Add(p);
                    points.Add(p);
                }
                FlowAxisMax = qMax.ToString("0") + " m³/h";
                HeadAxisMax = hMax.ToString("0") + " m";
            }
            else
            {
                FlowAxisMax = HeadAxisMax = "";
            }
            HeadCurve = curve;
            HeadPoints = points;
            OnPropertyChanged(nameof(HeadCurve));
            OnPropertyChanged(nameof(HeadPoints));
            OnPropertyChanged(nameof(FlowAxisMax));
            OnPropertyChanged(nameof(HeadAxisMax));
        }

        private static double NiceMax(double v)
        {
            if (v <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (var m in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
                if (v <= m * mag) return m * mag;
            return 10 * mag;
        }
    }
}
