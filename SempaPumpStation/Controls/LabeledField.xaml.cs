using System.Windows;
using System.Windows.Controls;
using TerakkiFramework.Plc.Abstractions;

namespace SempaPumpStation.Controls
{
    public partial class LabeledField : UserControl
    {
        public LabeledField() { InitializeComponent(); }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(LabeledField), new PropertyMetadata(""));
        public static readonly DependencyProperty FieldTagProperty =
            DependencyProperty.Register(nameof(FieldTag), typeof(IPlcTag), typeof(LabeledField), new PropertyMetadata(null));
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(LabeledField), new PropertyMetadata(""));
        public static readonly DependencyProperty FormatProperty =
            DependencyProperty.Register(nameof(Format), typeof(string), typeof(LabeledField), new PropertyMetadata("0.##"));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public IPlcTag FieldTag { get => (IPlcTag)GetValue(FieldTagProperty); set => SetValue(FieldTagProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    }
}
