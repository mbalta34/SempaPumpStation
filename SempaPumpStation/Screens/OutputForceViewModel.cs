using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiUI.Dialogs;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>One digital output: live state, forcing, and the Force ON / Force OFF / Release commands.</summary>
    public sealed class OutputForceRow : ViewModelBase
    {
        public DigitalOutputInfo Info { get; set; }
        public string Label => Info.Label;
        public string Name => Info.Name;
        public ICommand ForceOnCommand { get; set; }
        public ICommand ForceOffCommand { get; set; }
        public ICommand ReleaseCommand { get; set; }

        private bool _pv, _forcing;
        public bool Pv { get => _pv; set => Set(ref _pv, value); }
        public bool Forcing { get => _forcing; set => Set(ref _forcing, value); }
    }

    public sealed class OutputForceGroup
    {
        public string Title { get; set; }
        public ObservableCollection<OutputForceRow> Rows { get; } = new ObservableCollection<OutputForceRow>();
    }

    /// <summary>
    /// Digital output force table (as in BluePrism's maintenance HMI): every output of "Io".DigitalOutputs with its
    /// state, Force ON / Force OFF (FB_Io_DigitalOutput holds the output until released) and Release. Forcing
    /// bypasses the station program's interlocks, so each force is confirmed and needs an engineer login.
    /// </summary>
    public sealed class OutputForceViewModel : ScreenBase
    {
        public override string Key => "OutputForce";
        public override string Title => "Çıkış Zorlama";

        public ObservableCollection<OutputForceGroup> Groups { get; } = new ObservableCollection<OutputForceGroup>();
        public ICommand ReleaseAllCommand { get; }

        /// <summary>Asks before forcing; the smoke test replaces it.</summary>
        public Func<string, bool> Confirm { get; set; }

        private int _forcedCount;
        public int ForcedCount { get => _forcedCount; private set { if (Set(ref _forcedCount, value)) OnPropertyChanged(nameof(ForcedText)); } }
        public string ForcedText => _forcedCount == 0 ? "Zorlanan çıkış yok" : _forcedCount + " çıkış zorlanıyor";

        public OutputForceViewModel(HmiContext ctx) : base(ctx)
        {
            Confirm = question => ctx.Dialogs.ShowYesNoAsync(question, "Çıkış Zorlama", yesText: "Evet", noText: "Hayır").Result == DialogResult.Yes;

            for (int z = 1; z <= Station.ZoneCount; z++) Groups.Add(Group("BÖLGE " + z, z));
            Groups.Add(Group("SÜRÜCÜLER (MERKEZİ PANO)", 0));

            ReleaseAllCommand = new SyncCommand(ReleaseAll);
            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private OutputForceGroup Group(string title, int zone)
        {
            var group = new OutputForceGroup { Title = title };
            foreach (var info in DigitalOutputs.OfZone(zone))
            {
                var o = info;
                group.Rows.Add(new OutputForceRow
                {
                    Info = o,
                    ForceOnCommand = new SyncCommand(() => Force(o, true)),
                    ForceOffCommand = new SyncCommand(() => Force(o, false)),
                    ReleaseCommand = new SyncCommand(() => Ctx.Click(o.Hp + "StopForcing")),
                });
            }
            return group;
        }

        private void Force(DigitalOutputInfo o, bool on)
        {
            string where = o.Zone > 0 ? "Bölge " + o.Zone + " — " : "";
            string question = where + o.Label + " çıkışı ZORLA " + (on ? "AÇIK (1)" : "KAPALI (0)") + " yapılsın mı?\n\n"
                            + "Zorlama, programın kilitlemelerini devre dışı bırakır. Çıkış, Bırak'a basılana kadar bu değerde kalır.";
            if (!Confirm(question)) return;
            Ctx.Click(o.Hp + (on ? "ForceOn" : "ForceOff"));
        }

        private void ReleaseAll()
        {
            foreach (var row in Groups.SelectMany(g => g.Rows).Where(r => r.Forcing))
                Ctx.Click(row.Info.Hp + "StopForcing");
        }

        private void Refresh()
        {
            int forced = 0;
            foreach (var row in Groups.SelectMany(g => g.Rows))
            {
                row.Pv = Ctx.GetBool(row.Info.Ph + "Pv");
                row.Forcing = Ctx.GetBool(row.Info.Ph + "ForcingActive");
                if (row.Forcing) forced++;
            }
            ForcedCount = forced;
        }
    }
}
