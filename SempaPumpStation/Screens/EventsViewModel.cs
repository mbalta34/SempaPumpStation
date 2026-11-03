using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Screens
{
    public sealed class EventsViewModel : ScreenBase
    {
        public override string Key => "Events";
        public override string Title => "Olay Kaydı";

        public AlarmMonitor Alarms { get; }

        public EventsViewModel(HmiContext ctx, AlarmMonitor alarms) : base(ctx)
        {
            Alarms = alarms;
        }
    }
}
