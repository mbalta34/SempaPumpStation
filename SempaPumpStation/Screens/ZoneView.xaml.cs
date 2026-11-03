using System.Windows;
using System.Windows.Controls;

namespace SempaPumpStation.Screens
{
    public partial class ZoneView : UserControl
    {
        public ZoneView() { InitializeComponent(); }

        private void Instruments_Click(object sender, RoutedEventArgs e) =>
            (DataContext as ZoneViewModel)?.Context.Navigate("Instruments");
    }
}
