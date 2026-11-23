using System;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// Port of FB_Communication_ValueFilter with the settings of FB_Communication_PowermeterFilterHmi: the filter of an
    /// energy meter (FB_Communication_PowermeterFilter) or of the drive (FB_Communication_DriveFilter). One setting for all
    /// values of the device: none, EMA, or EMA with outlier rejection (the jump in % of the larger of the new and the filtered
    /// value, since the values have different units), then an optional moving average; a value every 100 ms.
    /// </summary>
    public sealed class PowermeterFilter
    {
        /// <summary>A meter's filtered values, in the PLC's order (General V, I, P, Q, S, PF, Hz; phases V A-B, B-C, C-A; phases I).</summary>
        public static readonly string[] Values =
        {
            "General.Voltage", "General.Current", "General.ActivePower", "General.ReactivePower", "General.ApparentPower",
            "General.PowerFactor", "General.Frequency",
            "PhaseA.Voltage", "PhaseB.Voltage", "PhaseC.Voltage",
            "PhaseA.Current", "PhaseB.Current", "PhaseC.Current",
        };

        /// <summary>The drive's filtered values (not the status word), in the PLC's order: main request, then the second one.</summary>
        public static readonly string[] DriveValues =
        {
            "OutputFrequency", "MotorCurrent", "MotorTorque", "MainsVoltage", "MotorVoltage", "MotorPower",
            "MotorTorqueNm", "MotorMechanicalPower", "OutputElectricalPower",
        };

        private const int MaxValues = 16;

        private readonly string _hp, _ph;
        public readonly HmiParameterInt FilterType = new HmiParameterInt(), FilterAverageCount = new HmiParameterInt();
        public readonly HmiParameterReal FilterEmaAlpha = new HmiParameterReal(), FilterOutlierMaxJump = new HmiParameterReal();
        public readonly HmiParameterInt FilterOutlierMaxRejectCount = new HmiParameterInt();

        private bool _seeded;
        private readonly double[] _ema = new double[MaxValues], _emaOutlier = new double[MaxValues];
        private readonly int[] _rejectCount = new int[MaxValues];
        private readonly double[,] _buffer = new double[AnalogFilter.AverageMax, MaxValues];
        private int _index, _filled;

        /// <summary>The filter of the meter on a power source.</summary>
        public PowermeterFilter(int source) : this(Station.MeterFilterHp(source), Station.MeterFilterPh(source)) { }

        /// <param name="hp">The settings' HMI-to-PLC prefix ("StationHp.Powermeters[n]." / "StationHp.Drive630.").</param>
        public PowermeterFilter(string hp, string ph)
        {
            _hp = hp;
            _ph = ph;
            FilterEmaAlpha.RetainValue = AnalogFilter.DefaultEmaAlpha;
            FilterOutlierMaxJump.RetainValue = AnalogFilter.DefaultMeterOutlierMaxJump;
            FilterOutlierMaxRejectCount.RetainValue = AnalogFilter.DefaultOutlierMaxRejectCount;
        }

        /// <param name="raw">The decoded values (at most 16).</param>
        /// <param name="valid">The device answers; otherwise the values pass unchanged and the filters start again afterwards.</param>
        /// <param name="sample">The 100 ms pulse: the filters take a new value.</param>
        public double[] Run(PlcTags t, double[] raw, bool valid, bool sample)
        {
            FilterType.Run(t, _hp + "FilterType", _ph + "FilterType", 0, true, true);
            FilterEmaAlpha.Run(t, _hp + "FilterEmaAlpha", _ph + "FilterEmaAlpha", 0, true, true);
            FilterAverageCount.Run(t, _hp + "FilterAverageCount", _ph + "FilterAverageCount", 0, true, true);
            FilterOutlierMaxJump.Run(t, _hp + "FilterOutlierMaxJump", _ph + "FilterOutlierMaxJump", 0, true, true);
            FilterOutlierMaxRejectCount.Run(t, _hp + "FilterOutlierMaxRejectCount", _ph + "FilterOutlierMaxRejectCount", 0, true, true);

            // FB_Communication_PowermeterFilterHmi: values that cannot be meant fall back to the defaults
            if (FilterEmaAlpha.Value <= 0.0 || FilterEmaAlpha.Value > 1.0) FilterEmaAlpha.Value = AnalogFilter.DefaultEmaAlpha;
            if (FilterOutlierMaxJump.Value <= 0.0) FilterOutlierMaxJump.Value = AnalogFilter.DefaultMeterOutlierMaxJump;
            if (FilterOutlierMaxRejectCount.Value <= 0) FilterOutlierMaxRejectCount.Value = AnalogFilter.DefaultOutlierMaxRejectCount;
            if (FilterAverageCount.Value < 0) FilterAverageCount.Value = 0;
            else if (FilterAverageCount.Value > AnalogFilter.AverageMax) FilterAverageCount.Value = AnalogFilter.AverageMax;

            int n = Math.Min(raw.Length, MaxValues);
            var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = double.IsNaN(raw[i]) ? 0.0 : raw[i];

            if (!valid)
            {
                _seeded = false;
                _filled = 0;
                return v;
            }

            if (!_seeded)
            {
                Array.Copy(v, _ema, n);
                Array.Copy(v, _emaOutlier, n);
                Array.Clear(_rejectCount, 0, MaxValues);
                _filled = 0;
                _seeded = true;
            }

            double alpha = FilterEmaAlpha.Value;
            if (sample)
            {
                for (int i = 0; i < n; i++)
                {
                    _ema[i] = alpha * v[i] + (1.0 - alpha) * _ema[i];

                    double reference = Math.Max(Math.Abs(v[i]), Math.Abs(_emaOutlier[i]));
                    if (Math.Abs(v[i] - _emaOutlier[i]) > FilterOutlierMaxJump.Value / 100.0 * reference)
                    {
                        if (++_rejectCount[i] >= FilterOutlierMaxRejectCount.Value)
                        {
                            _emaOutlier[i] = v[i];   // accept as new level
                            _rejectCount[i] = 0;
                        }
                    }
                    else
                    {
                        _emaOutlier[i] = alpha * v[i] + (1.0 - alpha) * _emaOutlier[i];
                        _rejectCount[i] = 0;
                    }
                }
            }

            var y = new double[n];
            for (int i = 0; i < n; i++)
                y[i] = FilterType.Value == AnalogFilter.Ema ? _ema[i] : FilterType.Value == AnalogFilter.EmaOutlier ? _emaOutlier[i] : v[i];

            if (sample)
            {
                _index = (_index + 1) % AnalogFilter.AverageMax;
                for (int i = 0; i < n; i++) _buffer[_index, i] = y[i];
                if (_filled < AnalogFilter.AverageMax) _filled++;
            }

            int count = Math.Min(FilterAverageCount.Value, _filled);
            if (FilterAverageCount.Value >= 2 && count >= 1)
            {
                for (int i = 0; i < n; i++)
                {
                    double sum = 0.0;
                    int row = _index;
                    for (int k = 0; k < count; k++)
                    {
                        sum += _buffer[row, i];
                        row = row == 0 ? AnalogFilter.AverageMax - 1 : row - 1;
                    }
                    y[i] = sum / count;
                }
            }
            return y;
        }
    }
}
