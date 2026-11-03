using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using SempaPumpStation.Screens;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Shell
{
    public sealed class ShellViewModel : ViewModelBase
    {
        private readonly HmiContext _ctx;

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

        private string _userName = "İsmet Y. (Operatör)";
        public string UserName { get => _userName; set => Set(ref _userName, value); }

        public ShellViewModel(HmiContext ctx)
        {
            _ctx = ctx;
            Alarms = new AlarmMonitor(ctx);

            NavigateCommand = new RelayCommand(p => { if (p is NavEntry e) Current = e.Screen; });
            AlarmResetCommand = new RelayCommand(() => Alarms.ResetAll());
            LoginCommand = new RelayCommand(() => ToggleUser());

            Add(MainNav, "Overview", "▦", "Genel Bakış", new OverviewViewModel(ctx));
            for (int z = 1; z <= Station.ZoneCount; z++)
                Add(MainNav, "Zone" + z, "⧉", "Bölge " + z, new ZoneViewModel(ctx, z));
            Add(MainNav, "Instruments", "◎", "Enstrümanlar", new InstrumentsViewModel(ctx));
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

            _ctx.Navigator = key =>
            {
                NavEntry e;
                if (_byKey.TryGetValue(key, out e)) Current = e.Screen;
            };

            Current = _all[0].Screen;
            Alarms.Start();
        }

        private void Add(ObservableCollection<NavEntry> group, string key, string icon, string label, ScreenBase screen)
        {
            var e = new NavEntry { Key = key, Icon = icon, Label = label, Screen = screen };
            group.Add(e);
            _all.Add(e);
            _byKey[key] = e;
        }

        private void ToggleUser()
        {
            UserName = UserName.StartsWith("İsmet") ? "Admin (Yönetici)" : "İsmet Y. (Operatör)";
            Alarms.LogEvent("Kullanıcı değişimi: " + UserName);
        }
    }
}
