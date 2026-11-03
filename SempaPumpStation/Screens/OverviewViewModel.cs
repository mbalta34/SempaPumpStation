using System.Collections.ObjectModel;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>Summary card of one zone on the overview.</summary>
    public sealed class ZoneSummary : ViewModelBase
    {
        public int Zone { get; set; }
        public string Name => "BÖLGE " + Zone;
        public TagScope Ph { get; set; }
        public TagScope Ai { get; set; }
        public ICommand OpenCommand { get; set; }

        private string _source, _state, _test;
        public string Source { get => _source; set => Set(ref _source, value); }
        public string State { get => _state; set => Set(ref _state, value); }
        public string Test { get => _test; set => Set(ref _test, value); }
    }

    /// <summary>One power source of the central cabinet and who uses it.</summary>
    public sealed class SourceSummary : ViewModelBase
    {
        public int Id { get; set; }
        public string Name { get; set; }

        private string _owner, _state;
        private bool _running;
        public string Owner { get => _owner; set => Set(ref _owner, value); }
        public string State { get => _state; set => Set(ref _state, value); }
        public bool Running { get => _running; set => Set(ref _running, value); }
    }

    public sealed class OverviewViewModel : ScreenBase
    {
        public override string Key => "Overview";
        public override string Title => "Genel Bakış";

        public ObservableCollection<ZoneSummary> Zones { get; } = new ObservableCollection<ZoneSummary>();
        public ObservableCollection<SourceSummary> Sources { get; } = new ObservableCollection<SourceSummary>();

        public OverviewViewModel(HmiContext ctx) : base(ctx)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                int zone = z;
                Zones.Add(new ZoneSummary
                {
                    Zone = z,
                    Ph = ctx.Scope(Station.ZonePh(z)),
                    Ai = ctx.Scope("MaintenancePh.Io.AnalogInputs.Zone" + z + "_"),
                    OpenCommand = new RelayCommand(() => Ctx.Navigate("Zone" + zone)),
                });
            }
            foreach (var s in Station.Sources) Sources.Add(new SourceSummary { Id = s.Id, Name = s.Name });

            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private void Refresh()
        {
            foreach (var z in Zones)
            {
                string ph = Station.ZonePh(z.Zone);
                z.Source = Station.Source(Ctx.GetInt(ph + "PowerSource.Value"))?.Name ?? "Kaynak seçilmedi";
                z.State = Ctx.GetBool(ph + "EStop") ? "ACİL STOP"
                        : Ctx.GetBool(ph + "DeviceFault") || Ctx.GetBool(ph + "DriveFault") ? "ARIZA"
                        : Ctx.GetBool(ph + "PumpRunning") ? "Pompa çalışıyor"
                        : Station.PowerStateText(Ctx.GetInt(ph + "PowerState"));
                z.Test = Ctx.GetBool(ph + "TestActive")
                    ? "Test aktif · " + Ctx.GetInt(ph + "SampleCount") + " numune"
                    : "Test yok";
            }

            foreach (var s in Sources)
            {
                int owner = 0;
                for (int z = 1; z <= Station.ZoneCount; z++)
                    if (Ctx.GetInt(Station.ZonePh(z) + "PowerSource.Value") == s.Id) owner = z;
                int fed = Ctx.FedZone(s.Id);
                s.Running = Ctx.SourceRunning(s.Id);
                s.Owner = owner > 0 ? "Bölge " + owner : "—";
                bool fault = Ctx.IsSimulation ? Ctx.GetBool(Station.SimDrive(s.Id) + "Fault")
                                              : owner > 0 && Ctx.GetBool(Station.ZonePh(owner) + "DriveFault");
                s.State = fault ? "Arıza"
                        : s.Running ? "Çalışıyor"
                        : fed > 0 ? "Bölge " + fed + "'e bağlı"
                        : owner > 0 ? "Bağlanıyor" : "Boşta";
            }
        }
    }
}
