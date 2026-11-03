using System.Windows;
using System.Windows.Controls;

namespace SempaPumpStation.Controls
{
    public partial class LabeledValue : UserControl
    {
        public LabeledValue() { InitializeComponent(); }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(LabeledValue), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(object), typeof(LabeledValue), new PropertyMetadata(null));
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(LabeledValue), new PropertyMetadata(""));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public object Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    }
}
