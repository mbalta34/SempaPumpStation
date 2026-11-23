using TerakkiUI.Components.Hmi;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// The filter types of FB_Io_AnalogInput (FILTER_NONE / FILTER_EMA / FILTER_EMA_OUTLIER) and of
    /// FB_Communication_ValueFilter (energy meters and the drive), as combo box items (Terakki's <see cref="HmiComboBoxItem"/>)
    /// for the tables that edit them. Value is the PLC's Int. Both blocks add an optional moving average over the
    /// last FilterAverageCount values (one per 100 ms) after the filter.
    /// </summary>
    public static class AnalogFilter
    {
        public const int None = 0, Ema = 1, EmaOutlier = 2;

        /// <summary>The longest moving average (AVERAGE_MAX): 100 values, 10 s.</summary>
        public const int AverageMax = 100;
        /// <summary>One value of the moving average per OB30 cycle / meter sample.</summary>
        public const double SampleSeconds = 0.1;

        // Retained parameters read 0 after a download that reinitialises the block; the PLC replaces values
        // that cannot be meant by these (DEFAULT_*)
        public const int DefaultMaxRaw = 27648, DefaultOutlierMaxRejectCount = 5;
        public const double DefaultEmaAlpha = 0.2, DefaultOutlierMaxJump = 1000.0;
        /// <summary>Energy meters and the drive: the outlier jump in % of the value (their values have different units).</summary>
        public const double DefaultMeterOutlierMaxJump = 20.0;

        public static readonly HmiComboBoxItem[] Items =
        {
            new HmiComboBoxItem { Display = "Yok", Value = None },
            new HmiComboBoxItem { Display = "EMA", Value = Ema },
            new HmiComboBoxItem { Display = "EMA + aykırı değer", Value = EmaOutlier },
        };

        /// <summary>The energy meters' and the drive's filter types (FB_Communication_ValueFilter: the same three).</summary>
        public static readonly HmiComboBoxItem[] MeterItems = Items;

        /// <summary>The window of a moving average as text: "Kapalı" or "20 (2.0 s)".</summary>
        public static string AverageText(int count) =>
            count < 2 ? "Kapalı" : count + " (" + (count * SampleSeconds).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s)";
    }
}
