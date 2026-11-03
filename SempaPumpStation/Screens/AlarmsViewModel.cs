using System.Windows.Input;
using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Screens
{
    public sealed class AlarmsViewModel : ScreenBase
    {
        public override string Key => "Alarms";
        public override string Title => "Alarmlar";

        public AlarmMonitor Alarms { get; }

        public ICommand ResetCommand { get; }

        public AlarmsViewModel(HmiContext ctx, AlarmMonitor alarms) : base(ctx)
        {
            Alarms = alarms;
            ResetCommand = new RelayCommand(() => Alarms.ResetAll());
        }
    }
}
