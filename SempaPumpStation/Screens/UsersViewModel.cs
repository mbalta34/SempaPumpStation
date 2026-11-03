using SempaPumpStation.Infrastructure;
using TerakkiUI.Components.User;

namespace SempaPumpStation.Screens
{
    /// <summary>User administration (administrators only): the TerakkiUI view model in the HMI's navigation.</summary>
    public sealed class UsersViewModel : ScreenBase
    {
        public override string Key => "Users";
        public override string Title => "Kullanıcılar";

        public UserManagementViewModel Manager { get; }

        public UsersViewModel(HmiContext ctx) : base(ctx)
        {
            Manager = new UserManagementViewModel(ctx.Security, ctx.Dialogs);
        }
    }
}
