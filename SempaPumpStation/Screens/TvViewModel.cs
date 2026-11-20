using System.Collections.ObjectModel;
using System.Globalization;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>
    /// The big display above a test zone (the old "TV EKRANI" page): Q, Hm, pump power and efficiency,
    /// with the sample (KAYIT) and test (DENEY) numbers, readable from across the hall.
    /// </summary>
    public sealed class TvViewModel : ScreenBase
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public override string Key => "Tv";
        public override string Title => "TV Ekranı";

        public ObservableCollection<ChoiceItem> Zones { get; } = new ObservableCollection<ChoiceItem>();

        private int _zone = 1;
        public int Zone
        {
            get => _zone;
            set
            {
                if (!Set(ref _zone, value)) return;
                foreach (var z in Zones) z.State = z.Value == value ? "active" : "";
                Refresh();
            }
        }

        private string _heading, _recordNo, _testNo, _flowText, _headText, _powerText, _efficiencyText, _statusText;
        public string Heading { get => _heading; private set => Set(ref _heading, value); }
        public string RecordNo { get => _recordNo; private set => Set(ref _recordNo, value); }
        public string TestNo { get => _testNo; private set => Set(ref _testNo, value); }
        public string FlowText { get => _flowText; private set => Set(ref _flowText, value); }
        public string HeadText { get => _headText; private set => Set(ref _headText, value); }
        public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
        public string EfficiencyText { get => _efficiencyText; private set => Set(ref _efficiencyText, value); }
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

        public TvViewModel(HmiContext ctx) : base(ctx)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                int zone = z;
                Zones.Add(new ChoiceItem { Value = z, Title = "Bölge " + z, State = z == 1 ? "active" : "", Command = new SyncCommand(() => Zone = zone) });
            }

            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void Refresh()
        {
            string ph = Station.ZonePh(_zone);
            var r = PumpCalc.Live(Ctx, _zone);

            Heading = "POMPA TESTİ MANUEL  ·  BÖLGE " + _zone;
            RecordNo = Ctx.GetInt(ph + "SampleCount").ToString(Inv);
            // The running test's number, or the one the next test will get
            TestNo = (Ctx.GetBool(ph + "TestActive") ? Ctx.GetInt(Station.Samples(_zone) + "TestNo") : Ctx.GetInt(Station.NextTestNo)).ToString(Inv);

            FlowText = "Q : " + Number(r.Flow) + " m³/h";
            HeadText = "Hm : " + Number(r.Head) + " mss";
            PowerText = "Pp : " + Number(r.PumpPower) + " kW";
            EfficiencyText = "Eff : " + Number(r.Efficiency) + " %";

            StatusText = Ctx.GetBool(ph + "EStop") ? "ACİL STOP"
                       : Ctx.GetBool(ph + "SafetyStop") ? "MÜDAHALE · TÜM MOTORLAR DURDURULDU"
                       : Ctx.GetBool(ph + "DeviceFault") || Ctx.GetBool(ph + "DriveFault") ? "ARIZA"
                       : Ctx.GetBool(ph + "TestActive") ? (Ctx.GetBool(ph + "PumpRunning") ? "TEST SÜRÜYOR · POMPA ÇALIŞIYOR" : "TEST SÜRÜYOR · POMPA DURUYOR")
                       : Ctx.GetBool(ph + "PumpRunning") ? "POMPA ÇALIŞIYOR"
                       : "TEST BEKLENİYOR";
        }

        /// <summary>Two decimals, as the old display; a value the station cannot measure yet shows a dash.</summary>
        private static string Number(double? v) => v.HasValue ? v.Value.ToString("0.00", Inv) : "—";
    }
}
