using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Plc.Abstractions;
using TerakkiUI.Components.Hmi.Graphics;

namespace SempaPumpStation.Screens
{
    public partial class TrendView : UserControl
    {
        private TrendViewModel _vm;

        public TrendView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => Attach(DataContext as TrendViewModel);
        }

        private void Attach(TrendViewModel vm)
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = vm;
            if (_vm == null) return;
            _vm.PropertyChanged += OnVmChanged;
            BuildSeries();
        }

        private void OnVmChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TrendViewModel.Zone)) BuildSeries();
        }

        // TrendSeries.Tag is a plain CLR property (not bindable in XAML), so wire the live series here
        private void BuildSeries()
        {
            Trend.Series.Clear();
            int z = _vm.Zone;
            Add(Station.ZonePh(z) + "Flow", "Debi", "#3B82F6");
            Add(Station.ZonePh(z) + "DischargePressure", "Basma basıncı (bar)", "#2ECC71");
            Add(Station.ZonePh(z) + "SuctionPressure", "Emme basıncı (bar)", "#F5A623");
            Add(Station.AiPh("Zone" + z + "_Vibration") + "Pv", "Titreşim (mm/s)", "#22C1D6");
        }

        private void Add(string tagName, string label, string color)
        {
            if (!_vm.Tags.TryGetValue(tagName, out IPlcTag tag) || tag == null)
                return;
            Trend.Series.Add(new TrendSeries
            {
                Tag = tag,
                Label = label,
                Brush = (Brush)new BrushConverter().ConvertFromString(color)
            });
        }
    }
}
