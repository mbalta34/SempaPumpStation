using System.Windows.Controls;

namespace SempaPumpStation.Screens
{
    public partial class AnalogSettingsView : UserControl
    {
        public AnalogSettingsView() { InitializeComponent(); }

        // While a cell is edited its row's parameters are not refreshed from the PLC, so the entry is not overwritten
        private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is AnalogSettingRow r) r.Editing = true;
        }

        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.Item is AnalogSettingRow r) r.Editing = false;
        }
    }
}
