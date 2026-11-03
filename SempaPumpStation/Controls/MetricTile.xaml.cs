using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SempaPumpStation.Controls
{
    public partial class MetricTile : UserControl
    {
        public MetricTile() { InitializeComponent(); }

        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(MetricTile), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(object), typeof(MetricTile), new PropertyMetadata(null));
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(MetricTile), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueBrushProperty =
            DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(MetricTile),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE7, 0xEE, 0xF5))));
        public static readonly DependencyProperty ValueSizeProperty =
            DependencyProperty.Register(nameof(ValueSize), typeof(double), typeof(MetricTile), new PropertyMetadata(30.0));

        public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
        public object Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
        public double ValueSize { get => (double)GetValue(ValueSizeProperty); set => SetValue(ValueSizeProperty, value); }
    }
}
