using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>Formats a (possibly boxed) numeric tag value with the format string in ConverterParameter.</summary>
    public sealed class TagFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            try
            {
                if (value is string s) return s;
                if (value is bool b) return b ? "1" : "0";
                double d = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d)) return "—";
                string fmt = parameter as string;
                return d.ToString(string.IsNullOrEmpty(fmt) ? "0.##" : fmt, CultureInfo.InvariantCulture);
            }
            catch { return value.ToString(); }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>bool → one of two strings supplied as "trueText|falseText" in ConverterParameter.</summary>
    public sealed class BoolTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = ToBool(value);
            var parts = (parameter as string ?? "ON|OFF").Split('|');
            return b ? parts[0] : (parts.Length > 1 ? parts[1] : "");
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

        private static bool ToBool(object v)
        {
            if (v is bool b) return b;
            if (v == null) return false;
            try { return System.Convert.ToBoolean(v); } catch { return false; }
        }
    }

    /// <summary>Alarm severity string → status brush (ALARM=red, WARN=amber, else green).</summary>
    public sealed class SeverityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "ALARM": return new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                case "WARN": return new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
                default: return new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
