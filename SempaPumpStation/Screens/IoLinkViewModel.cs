using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>One port of a module on the IO-Link screen.</summary>
    public sealed class IoLinkPortCell : ViewModelBase
    {
        public IoLinkPortInfo Info { get; set; }
        public string PortText => "Port " + Info.Port;
        public ICommand SelectCommand { get; set; }

        private string _title = "", _value = "", _raw = "", _state = "";
        public string Title { get => _title; set => Set(ref _title, value); }
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Raw { get => _raw; set => Set(ref _raw, value); }
        /// <summary>"" fixed sensor, "instrument" (device can be changed), "selected", "free", "absent" (drives the cell style).</summary>
        public string State { get => _state; set => Set(ref _state, value); }
    }

    /// <summary>The master or one of its hubs.</summary>
    public sealed class IoLinkModuleCard : ViewModelBase
    {
        public string TypeName { get; set; }
        public string Description { get; set; }
        public string Connection { get; set; }
        public ObservableCollection<IoLinkPortCell> Ports { get; } = new ObservableCollection<IoLinkPortCell>();
    }

    /// <summary>
    /// The IO-Link line of a zone as in the IO list: the BNI00L3 master and its hubs with what each port carries,
    /// live. Every sensor has a fixed port; on a flowmeter, pressure transmitter or vacuummeter port the user
    /// picks which library device is connected (the position's device selection, as on the instruments screen).
    /// </summary>
    public sealed class IoLinkViewModel : ScreenBase
    {
        public override string Key => "IoLink";
        public override string Title => "IO-Link Bağlantıları";

        public ObservableCollection<ChoiceItem> Zones { get; } = new ObservableCollection<ChoiceItem>();
        public ObservableCollection<IoLinkModuleCard> Hubs { get; } = new ObservableCollection<IoLinkModuleCard>();
        /// <summary>Library devices that can be connected to the selected port; Value = device number, 0 = none.</summary>
        public ObservableCollection<ChoiceItem> Devices { get; } = new ObservableCollection<ChoiceItem>();

        private IoLinkModuleCard _master;
        public IoLinkModuleCard Master { get => _master; private set => Set(ref _master, value); }

        private int _zone = 1;
        private IoLinkPortCell _selected;

        public IoLinkViewModel(HmiContext ctx) : base(ctx)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                int zone = z;
                Zones.Add(new ChoiceItem { Value = z, Title = "Bölge " + z, Command = new RelayCommand(() => ShowZone(zone)) });
            }
            ShowZone(1);
            ctx.Scanned += (o, e) => Refresh();
        }

        public string ZoneNote => _zone == 1
            ? "Bölge 1 ve 2 ortak debimetreleri (FM1–FM5) Bölge 2'nin BNI00AJ hub'ına bağlıdır."
            : _zone == 2 ? "FM1–FM5 Bölge 1 ve 2 tarafından ortak kullanılır." : "";

        private bool DataOk => Ctx.IsSimulation || Ctx.GetBool("PLC_COMM_OK");

        // ---------------------------------------------------------------- device panel
        private string _selectedTitle = "", _selectedDevice = "", _selectedHint = "";
        private bool _canAssign;
        public bool HasSelection => _selected != null;
        public string SelectedTitle { get => _selectedTitle; private set => Set(ref _selectedTitle, value); }
        public string SelectedDevice { get => _selectedDevice; private set => Set(ref _selectedDevice, value); }
        public string SelectedHint { get => _selectedHint; private set => Set(ref _selectedHint, value); }
        /// <summary>False while a zone measuring with the position runs (the PLC locks the device), or without PLC data.</summary>
        public bool CanAssign { get => _canAssign; private set => Set(ref _canAssign, value); }

        private void ShowZone(int zone)
        {
            _zone = zone;
            foreach (var z in Zones) z.State = z.Value == zone ? "active" : "";

            Master = Card(IoLinkModule.Master, "Bölge " + zone);
            Hubs.Clear();
            foreach (var hub in IoLinkLayout.Hubs) Hubs.Add(Card(hub, "Master port " + IoLinkLayout.MasterPort(hub)));

            Select(null);
            OnPropertyChanged(nameof(ZoneNote));
            Refresh();
        }

        private IoLinkModuleCard Card(IoLinkModule module, string connection)
        {
            var card = new IoLinkModuleCard
            {
                TypeName = IoLinkLayout.TypeName(_zone, module),
                Description = IoLinkLayout.Description(_zone, module),
                Connection = connection,
            };
            foreach (var info in IoLinkLayout.Ports(_zone, module))
            {
                var cell = new IoLinkPortCell { Info = info };
                cell.SelectCommand = new RelayCommand(() => Select(cell), () => info.IsInstrument);
                card.Ports.Add(cell);
            }
            return card;
        }

        private void Select(IoLinkPortCell cell)
        {
            _selected = cell;
            Devices.Clear();
            if (cell != null)
            {
                var position = Station.Position(cell.Info.Position);
                Devices.Add(new ChoiceItem { Value = 0, Title = "Takılı değil", Command = new RelayCommand(() => Mount(0)) });
                for (int d = 1; d <= Station.LibrarySize; d++)
                {
                    int device = d;
                    string entry = Station.LibraryEntry(position.Category, d);
                    string description = Ctx.GetString(entry + "Description");
                    if (description.Length == 0) continue;   // only the devices entered in the library
                    Devices.Add(new ChoiceItem
                    {
                        Value = d,
                        Title = "#" + d + "  " + description,
                        Command = new RelayCommand(() => Mount(device)),
                    });
                }
            }
            OnPropertyChanged(nameof(HasSelection));
            Refresh();
        }

        /// <summary>The device connected to the selected port; the PLC rejects one that is mounted elsewhere.</summary>
        private void Mount(int device)
        {
            if (_selected == null || !CanAssign) return;
            Ctx.SetValue(Station.PositionHp(_selected.Info.Position) + "Device.Value", (short)device);
        }

        // ---------------------------------------------------------------- live values
        private void Refresh()
        {
            if (Master == null) return;
            foreach (var cell in Master.Ports) RefreshMaster(cell);
            foreach (var card in Hubs)
                foreach (var cell in card.Ports) RefreshHub(cell);
            if (_selected != null) RefreshPanel();
        }

        private void RefreshPanel()
        {
            var position = Station.Position(_selected.Info.Position);
            string ph = Station.PositionPh(position.Name);
            int mounted = Ctx.GetInt(ph + "Device.Value");

            // Where each library device of this category is mounted now
            var mountedAt = Station.Positions.Where(p => p.Category == position.Category && p.Name != position.Name)
                                             .Select(p => new { p, Device = Ctx.GetInt(Station.PositionPh(p.Name) + "Device.Value") })
                                             .Where(x => x.Device > 0)
                                             .ToDictionary(x => x.Device, x => PositionText(x.p));
            foreach (var d in Devices)
            {
                bool elsewhere = d.Value > 0 && mountedAt.ContainsKey(d.Value);
                d.State = d.Value == mounted ? "active" : elsewhere ? "held" : "";
                d.Subtitle = d.Value == 0 ? "" : elsewhere ? "Takılı: " + mountedAt[d.Value] : Range(position.Category, d.Value);
            }

            CanAssign = DataOk && Ctx.GetBool(ph + "Device.Enabled");
            SelectedTitle = IoLinkLayout.TypeName(_zone, _selected.Info.Module) + " · Port " + _selected.Info.Port + " — " + PositionText(position);
            SelectedDevice = mounted > 0 ? "#" + mounted + "  " + DeviceText(position.Category, mounted) : "Cihaz takılı değil";
            SelectedHint = !DataOk ? "PLC verisi yok; seçim yapılamaz."
                         : !Ctx.GetBool(ph + "Device.Enabled") ? "Bu pozisyonu kullanan bölge çalışıyor; cihaz değiştirilemez."
                         : Ctx.GetBool(ph + "SelectionRejected") ? "Seçim reddedildi: o cihaz başka bir pozisyonda takılı."
                         : "Bu porta takılı cihazı seçin. Başka pozisyonda takılı cihazlar seçilemez.";
        }

        private void RefreshMaster(IoLinkPortCell cell)
        {
            cell.Title = cell.Info.Label.Length > 0 ? cell.Info.Label : "Boş";
            cell.State = cell.Info.Label.Length > 0 ? "" : "free";
            if (cell.Info.Port == 3)
                cell.Value = !DataOk ? "veri yok"
                           : Number(Station.AiPh("Zone" + _zone + "_Vibration") + "Pv", "0.00") + " mm/s · "
                             + Number(Station.AiPh("Zone" + _zone + "_Temperature") + "Pv", "0.0") + " °C";
            else cell.Value = cell.Info.Port <= 2 ? IoLinkLayout.Description(_zone, IoLinkLayout.Hubs[cell.Info.Port]) : "";
        }

        private void RefreshHub(IoLinkPortCell cell)
        {
            var info = cell.Info;
            if (!info.Present)
            {
                cell.Title = "—"; cell.Value = ""; cell.Raw = ""; cell.State = "absent";
                return;
            }

            cell.Title = info.Label.Length > 0 ? info.Label : "Boş";
            cell.State = cell == _selected ? "selected" : info.IsInstrument ? "instrument" : info.Label.Length > 0 ? "" : "free";

            if (info.Module == IoLinkModule.DioHub)
            {
                cell.Value = DataOk ? DioState(info.Port) : "";
                return;
            }

            if (!DataOk)
            {
                cell.Raw = ""; cell.Value = info.Label.Length > 0 ? "veri yok" : "";
                return;
            }

            int raw = Ctx.GetInt(info.RawTag);
            cell.Raw = raw == IoLinkLayout.OpenInputRaw ? "açık devre" : "ham " + raw;

            if (info.IsInstrument)
            {
                var position = Station.Position(info.Position);
                int device = Ctx.GetInt(Station.PositionPh(position.Name) + "Device.Value");
                cell.Value = device > 0 ? DeviceText(position.Category, device) + " · " + PositionValue(position, device) : "Cihaz takılı değil";
            }
            else if (info.Channel != null)
                cell.Value = Number(Station.AiPh(info.Channel) + "Pv", "0.0") + " " + info.Unit;
            else cell.Value = "";
        }

        private static string PositionText(InstrumentPositionInfo p) =>
            "Bölge " + string.Join("-", p.Zones) + " · " + (p.Category == Station.Vacuummeter ? "Vakummetre" : p.Label);

        private string DeviceText(int category, int device)
        {
            string description = Ctx.GetString(Station.LibraryEntry(category, device) + "Description");
            return description.Length > 0 ? description : "(tanımsız)";
        }

        private string Range(int category, int device)
        {
            string e = Station.LibraryEntry(category, device);
            return Number(e + "MinPv", "0.##") + " … " + Number(e + "MaxPv", "0.##") + " " + Ctx.GetString(e + "Unit");
        }

        // The sensor's measured value with the unit of its mounted library device
        private string PositionValue(InstrumentPositionInfo position, int device)
        {
            string ai = Station.AiPh(position.Channel);
            if (Ctx.GetBool(ai + "WireBreak")) return "Tel kopuk";
            string unit = Ctx.GetString(Station.LibraryEntry(position.Category, device) + "Unit");
            return Number(ai + "Pv", position.Category == Station.Flowmeter ? "0.0" : "0.000") + " " + unit;
        }

        private string DioState(int port)
        {
            string ph = Station.ZonePh(_zone);
            switch (port)
            {
                case 0: return Ctx.GetBool(ph + "LampGreen") ? "Yanıyor" : "Sönük";
                case 2: return Ctx.GetBool(ph + "LampYellow") ? "Yanıyor" : "Sönük";
                case 4: return Ctx.GetBool(ph + "LampRed") ? "Yanıyor" : "Sönük";
                case 7: return Ctx.GetBool(ph + "EStop") ? "ACİL STOP" : "Normal";
                default: return "";
            }
        }

        private string Number(string tag, string format) => Ctx.GetDouble(tag).ToString(format, CultureInfo.InvariantCulture);
    }
}
