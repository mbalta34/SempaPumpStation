using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm.Commands;

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
            ResetCommand = new SyncCommand(() => Alarms.ResetAll());
        }
    }
}
