using System.Collections.ObjectModel;
using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Screens
{
    public sealed class TrendViewModel : ScreenBase
    {
        public override string Key => "Trend";
        public override string Title => "Trend";

        public ObservableCollection<ChoiceItem> Zones { get; } = new ObservableCollection<ChoiceItem>();

        private int _zone = 1;
        public int Zone
        {
            get => _zone;
            private set
            {
                if (!Set(ref _zone, value)) return;
                foreach (var z in Zones) z.State = z.Value == value ? "active" : "";
            }
        }

        public TrendViewModel(HmiContext ctx) : base(ctx)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                int zone = z;
                Zones.Add(new ChoiceItem { Value = z, Title = "Bölge " + z, State = z == 1 ? "active" : "", Command = new RelayCommand(() => Zone = zone) });
            }
        }
    }
}
