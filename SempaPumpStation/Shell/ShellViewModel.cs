using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using SempaPumpStation.Screens;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Security;
using TerakkiUI.Components.User;
using TerakkiUI.Navigation;

namespace SempaPumpStation.Shell
{
    public sealed class ShellViewModel : ViewModelBase
    {
        private const string LoginModal = "Login";
        private const string ChangePasswordModal = "ChangePassword";

        private readonly HmiContext _ctx;
        private readonly NavigationService _modals = new NavigationService();
        /// <summary>Logs the user out after config.xml AutoLogoutMinutes without input.</summary>
        public IdleLogout IdleLogout { get; }

        public HmiContext Context => _ctx;
        public AlarmMonitor Alarms { get; }

        public ObservableCollection<NavEntry> MainNav { get; } = new ObservableCollection<NavEntry>();
        public ObservableCollection<NavEntry> RecipeNav { get; } = new ObservableCollection<NavEntry>();
        public ObservableCollection<NavEntry> DiagNav { get; } = new ObservableCollection<NavEntry>();

        private readonly List<NavEntry> _all = new List<NavEntry>();
        private readonly Dictionary<string, NavEntry> _byKey = new Dictionary<string, NavEntry>();

        public ICommand NavigateCommand { get; }
        public ICommand AlarmResetCommand { get; }
        public ICommand LoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ChangePasswordCommand { get; }

        public UserSecurity Security => _ctx.Security;
        public CurrentUserContext User => _ctx.Security.User;

        private string _lastUsername = "";
        private bool _forcePasswordChange;
        private LoginViewModel _login;
        private NavEntry _usersEntry;

        private ScreenBase _current;
        public ScreenBase Current
        {
            get => _current;
            private set
            {
                if (Set(ref _current, value))
                {
                    foreach (var e in _all) e.IsActive = e.Screen == value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public string Title => _current?.Title ?? "";

        public ShellViewModel(HmiContext ctx)
        {
            _ctx = ctx;
            Alarms = new AlarmMonitor(ctx);

            NavigateCommand = new RelayCommand(p => { if (p is NavEntry e && e.IsVisible) Current = e.Screen; });
            AlarmResetCommand = new RelayCommand(() => Alarms.ResetAll());
            LoginCommand = new RelayCommand(ShowLogin);
            LogoutCommand = new RelayCommand(() => Security.Auth.Logout(Security.Texts.LogLogout));
            ChangePasswordCommand = new RelayCommand(() => ShowChangePassword(false));

            // TerakkiUI modals in the HMI's theme
            _modals.RegisterModal(LoginModal, () => new LoginWindow(), () => _login = new LoginViewModel(Security, _lastUsername));
            _modals.RegisterModal(ChangePasswordModal, () => new ChangePasswordWindow(), () => new ChangePasswordViewModel(Security, _forcePasswordChange));

            Add(MainNav, "Overview", "▦", "Genel Bakış", new OverviewViewModel(ctx));
            for (int z = 1; z <= Station.ZoneCount; z++)
                Add(MainNav, "Zone" + z, "⧉", "Bölge " + z, new ZoneViewModel(ctx, z));
            Add(MainNav, "Instruments", "◎", "Enstrümanlar", new InstrumentsViewModel(ctx));
            Add(MainNav, "IoLink", "⇄", "IO-Link", new IoLinkViewModel(ctx));
            Add(MainNav, "Report", "≣", "Deney Raporu", new ReportViewModel(ctx));

            Add(RecipeNav, "FirmaRecipe", "◆", "Firma Reçetesi", new FirmaRecipeViewModel(ctx));
            Add(RecipeNav, "PompaRecipe", "◆", "Pompa Reçetesi", new PompaRecipeViewModel(ctx));
            Add(RecipeNav, "MotorRecipe", "◆", "Motor Reçetesi", new MotorRecipeViewModel(ctx));
            Add(RecipeNav, "HatRecipe", "◆", "Hat Geometrisi", new HatRecipeViewModel(ctx));
            Add(RecipeNav, "TestiYapan", "◆", "Testi Yapan", new TestiYapanRecipeViewModel(ctx));

            Add(DiagNav, "Alarms", "⚠", "Alarmlar", new AlarmsViewModel(ctx, Alarms));
            Add(DiagNav, "Events", "≡", "Olay Kaydı", new EventsViewModel(ctx, Alarms));
            Add(DiagNav, "Trend", "∿", "Trend", new TrendViewModel(ctx));
            Add(DiagNav, "UserLog", "◔", "Kullanıcı Kaydı", new UserLogViewModel(ctx));
            _usersEntry = Add(DiagNav, "Users", "☺", "Kullanıcılar", new UsersViewModel(ctx));

            _ctx.Navigator = key =>
            {
                NavEntry e;
                if (_byKey.TryGetValue(key, out e) && e.IsVisible) Current = e.Screen;
            };

            Current = _all[0].Screen;
            Alarms.Start();

            User.Changed += (s, e) => { OnUserChanged(); Alarms.LogEvent("Kullanıcı: " + User.DisplayText); };
            IdleLogout = new IdleLogout(Security, ctx.AutoLogout);
            OnUserChanged();
        }

        private void OnUserChanged()
        {
            _usersEntry.IsVisible = User.CanManageUsers;
            if (_current == _usersEntry.Screen && !_usersEntry.IsVisible) Current = _all[0].Screen;
        }

        private void ShowLogin()
        {
            _login = null;
            _modals.ShowModal(LoginModal);   // returns when the window has closed
            if (_login == null || !_login.Succeeded) return;

            _lastUsername = User.Account.Username;
            if (Security.Auth.PasswordChangeRequired) ShowChangePassword(true);
        }

        private void ShowChangePassword(bool forced)
        {
            if (!User.IsAuthenticated) return;
            _forcePasswordChange = forced;
            _modals.ShowModal(ChangePasswordModal);
        }

        private NavEntry Add(ObservableCollection<NavEntry> group, string key, string icon, string label, ScreenBase screen)
        {
            var e = new NavEntry { Key = key, Icon = icon, Label = label, Screen = screen };
            group.Add(e);
            _all.Add(e);
            _byKey[key] = e;
            return e;
        }
    }
}
