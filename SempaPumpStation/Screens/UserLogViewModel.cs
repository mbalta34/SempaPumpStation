using System.Collections.ObjectModel;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Security;

namespace SempaPumpStation.Screens
{
    public sealed class UserLogViewModel : ScreenBase
    {
        public override string Key => "UserLog";
        public override string Title => "Kullanıcı Kaydı";

        public ObservableCollection<UserLogRow> Entries => Ctx.Security.Log.Entries;

        public UserLogViewModel(HmiContext ctx) : base(ctx)
        {
        }
    }
}
