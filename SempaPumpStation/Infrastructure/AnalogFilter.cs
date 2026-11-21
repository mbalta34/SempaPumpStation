using TerakkiUI.Components.Hmi;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The filter types of FB_Io_AnalogInput (FILTER_NONE / FILTER_EMA / FILTER_EMA_OUTLIER), as combo box items
    /// (Terakki's <see cref="HmiComboBoxItem"/>) for the tables that edit them. Value is the PLC's Int.
    /// </summary>
    public static class AnalogFilter
    {
        public const int None = 0, Ema = 1, EmaOutlier = 2;

        public static readonly HmiComboBoxItem[] Items =
        {
            new HmiComboBoxItem { Display = "Yok", Value = None },
            new HmiComboBoxItem { Display = "EMA", Value = Ema },
            new HmiComboBoxItem { Display = "EMA + aykırı değer", Value = EmaOutlier },
        };
    }
}
