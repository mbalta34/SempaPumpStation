using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;
using TerakkiUI.Dialogs;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>One digital input or output: live state, forcing, and the Force ON / Force OFF / Release commands.</summary>
    public sealed class ForceRow : ViewModelBase
    {
        public IForceableChannel Info { get; set; }
        public string Label => Info.Label;
        public string Name => Info.Name;
        public ICommand ForceOnCommand { get; set; }
        public ICommand ForceOffCommand { get; set; }
        public ICommand ReleaseCommand { get; set; }

        private bool _pv, _forcing, _wireBreak;
        public bool Pv { get => _pv; set => Set(ref _pv, value); }
        public bool Forcing { get => _forcing; set => Set(ref _forcing, value); }
        /// <summary>Inputs only: the input reports a wire break.</summary>
        public bool WireBreak { get => _wireBreak; set => Set(ref _wireBreak, value); }
    }

    public sealed class ForceGroup
    {
        public string Title { get; set; }
        public ObservableCollection<ForceRow> Rows { get; } = new ObservableCollection<ForceRow>();
    }

    /// <summary>
    /// Digital input or output force table (as in BluePrism's maintenance HMI): every channel of "Io".DigitalInputs or
    /// "Io".DigitalOutputs with its state, Force ON / Force OFF (FB_Io_DigitalInput / FB_Io_DigitalOutput hold the value
    /// until released) and Release. Forcing bypasses the field or the station program's interlocks, so each force is
    /// confirmed and needs an engineer login.
    /// </summary>
    public sealed class ForceViewModel : ScreenBase
    {
        private readonly bool _inputs;
        private readonly string _noun;   // "giriş" / "çıkış"

        public override string Key => _inputs ? "InputForce" : "OutputForce";
        public override string Title => _inputs ? "Giriş Zorlama" : "Çıkış Zorlama";

        public string Heading => _inputs ? "DİJİTAL GİRİŞ ZORLAMA" : "DİJİTAL ÇIKIŞ ZORLAMA";
        public string Description => _inputs
            ? "Zorlanan giriş, sahadaki sinyalden bağımsız olarak Bırak'a basılana kadar o değerde kalır; program onu gerçek sinyal gibi kullanır (ör. geri bildirim, acil stop, pandant). Zorlama mühendis girişi ister; her zorlanan giriş alarm listesinde uyarı olarak görünür. PLC yeniden başladığında zorlamalar kalkar."
            : "Zorlanan çıkış, programın kilitlemelerinden bağımsız olarak Bırak'a basılana kadar o değerde kalır. Zorlama mühendis girişi ister; her zorlanan çıkış alarm listesinde uyarı olarak görünür. PLC yeniden başladığında zorlamalar kalkar.";
        public string ForceOnTip => _inputs ? "Girişi 1'e zorla" : "Çıkışı 1'e zorla";
        public string ForceOffTip => _inputs ? "Girişi 0'a zorla" : "Çıkışı 0'a zorla";
        public string ReleaseTip => _inputs ? "Zorlamayı kaldır, giriş sahayı izler" : "Zorlamayı kaldır, çıkış programa döner";

        public ObservableCollection<ForceGroup> Groups { get; } = new ObservableCollection<ForceGroup>();
        public ICommand ReleaseAllCommand { get; }

        /// <summary>Asks before forcing; the smoke test replaces it.</summary>
        public Func<string, bool> Confirm { get; set; }

        private int _forcedCount;
        public int ForcedCount { get => _forcedCount; private set { if (Set(ref _forcedCount, value)) OnPropertyChanged(nameof(ForcedText)); } }
        public string ForcedText => _forcedCount == 0 ? "Zorlanan " + _noun + " yok" : _forcedCount + " " + _noun + " zorlanıyor";

        /// <param name="inputs">The digital inputs; otherwise the digital outputs.</param>
        public ForceViewModel(HmiContext ctx, bool inputs) : base(ctx)
        {
            _inputs = inputs;
            _noun = inputs ? "giriş" : "çıkış";
            Confirm = question => ctx.Dialogs.ShowYesNoAsync(question, Title, yesText: "Evet", noText: "Hayır").Result == DialogResult.Yes;

            for (int z = 1; z <= Station.ZoneCount; z++) Groups.Add(Group("BÖLGE " + z, Channels(z)));
            Groups.Add(Group("SÜRÜCÜLER (MERKEZİ PANO)", Channels(0)));

            ReleaseAllCommand = new SyncCommand(ReleaseAll);
            ctx.Scanned += (o, e) => Refresh();
            Refresh();
        }

        private IEnumerable<IForceableChannel> Channels(int zone) =>
            _inputs ? DigitalInputs.OfZone(zone).Cast<IForceableChannel>() : DigitalOutputs.OfZone(zone).Cast<IForceableChannel>();

        private ForceGroup Group(string title, IEnumerable<IForceableChannel> channels)
        {
            var group = new ForceGroup { Title = title };
            foreach (var channel in channels)
            {
                var c = channel;
                group.Rows.Add(new ForceRow
                {
                    Info = c,
                    ForceOnCommand = new SyncCommand(() => Force(c, true)),
                    ForceOffCommand = new SyncCommand(() => Force(c, false)),
                    ReleaseCommand = new SyncCommand(() => Ctx.Click(c.Hp + "StopForcing")),
                });
            }
            return group;
        }

        private void Force(IForceableChannel c, bool on)
        {
            string where = c.Zone > 0 ? "Bölge " + c.Zone + " — " : "";
            string question = where + c.Label + " " + (_inputs ? "girişi" : "çıkışı") + " ZORLA " + (on ? "AÇIK (1)" : "KAPALI (0)") + " yapılsın mı?\n\n"
                            + (_inputs ? "Program, sahadaki sinyal yerine bu değeri kullanır (geri bildirim, acil stop ve pandant dahil). "
                                       : "Zorlama, programın kilitlemelerini devre dışı bırakır. ")
                            + (_inputs ? "Giriş" : "Çıkış") + ", Bırak'a basılana kadar bu değerde kalır.";
            if (!Confirm(question)) return;
            Ctx.Click(c.Hp + (on ? "ForceOn" : "ForceOff"));
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
                if (_inputs) row.WireBreak = Ctx.GetBool(row.Info.Ph + "WireBreak");
                if (row.Forcing) forced++;
            }
            ForcedCount = forced;
        }
    }
}
