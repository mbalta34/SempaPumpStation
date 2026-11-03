using System;
using System.Collections.ObjectModel;
using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Screens
{
    public sealed class UserLogRow
    {
        public string Time { get; set; }
        public string User { get; set; }
        public string Action { get; set; }
    }

    public sealed class UserLogViewModel : ScreenBase
    {
        public override string Key => "UserLog";
        public override string Title => "Kullanıcı Kaydı";

        public ObservableCollection<UserLogRow> Entries { get; }

        public UserLogViewModel(HmiContext ctx) : base(ctx)
        {
            Entries = new ObservableCollection<UserLogRow>
            {
                new UserLogRow { Time = Stamp(2),   User = "operator1", Action = "Operatör değişimi" },
                new UserLogRow { Time = Stamp(35),  User = "operator1", Action = "Giriş" },
                new UserLogRow { Time = Stamp(190), User = "mühendis",  Action = "Çıkış" },
                new UserLogRow { Time = Stamp(240), User = "mühendis",  Action = "Giriş" },
                new UserLogRow { Time = Stamp(520), User = "admin",     Action = "Çıkış" },
            };
        }

        private static string Stamp(int minutesAgo) =>
            DateTime.Now.AddMinutes(-minutesAgo).ToString("dd.MM.yyyy HH:mm:ss");
    }
}
