using System;
using System.Windows;
using System.Windows.Controls;

namespace SempaPumpStation.Screens
{
    public partial class PowermetersView : UserControl
    {
        public PowermetersView()
        {
            InitializeComponent();
        }

        // While a meter's or the drive's filter list is open, the PLC's value is not pushed into it
        private void Filter_DropDownOpened(object sender, EventArgs e) => SetEditing(sender, true);
        private void Filter_DropDownClosed(object sender, EventArgs e) => SetEditing(sender, false);

        private static void SetEditing(object sender, bool editing)
        {
            if ((sender as FrameworkElement)?.DataContext is FilterSettings filter) filter.Editing = editing;
        }
    }
}
