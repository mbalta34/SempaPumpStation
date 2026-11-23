using System;
using System.Collections.Generic;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// Port of FB_Io_AnalogInput with its FB_Io_AnalogInputHmi parameters (forcing left out):
    /// raw value → optional EMA / EMA + outlier filter → optional moving average → NORM_X / SCALE_X with the channel's
    /// MinRaw..MaxRaw / MinPv..MaxPv → × Coefficient + Offset. The parameters are retained HMI parameters, which is
    /// where FC_Station_LoadInstrument writes a library entry. The signal type gives the raw values as mA or V.
    /// </summary>
    public sealed class AnalogInputChannel
    {
        private const int RawOverflow = 32767, RawUnderflow = -32768;
        private const int FilterEma = 1, FilterEmaOutlier = 2;

        private readonly string _hp, _ph;
        public readonly HmiParameterReal MinPv = new HmiParameterReal(), MaxPv = new HmiParameterReal();
        public readonly HmiParameterInt MinRaw = new HmiParameterInt(), MaxRaw = new HmiParameterInt();
        public readonly HmiParameterInt FilterType = new HmiParameterInt();
        public readonly HmiParameterReal FilterEmaAlpha = new HmiParameterReal(), FilterOutlierMaxJump = new HmiParameterReal();
        public readonly HmiParameterInt FilterOutlierMaxRejectCount = new HmiParameterInt();
        public readonly HmiParameterReal Coefficient = new HmiParameterReal(), Offset = new HmiParameterReal();
        public readonly HmiParameterInt FilterAverageCount = new HmiParameterInt();

        private readonly double[] _averageBuffer = new double[AnalogFilter.AverageMax];
        private int _averageIndex, _averageFilled;

        private double _rawWithEma, _rawWithEmaOutlier;
        private int _rejectCount;
        private bool _firstCycle = true;

        public string Name { get; }
        public double Pv { get; private set; }
        /// <summary>The signalType input, as FB_Station_Instruments passes it (default 4..20 mA).</summary>
        public int SignalType { get; }

        public AnalogInputChannel(string name, double minPv = 0.0, double maxPv = 100.0)
        {
            Name = name;
            _hp = Station.AiHp(name);
            _ph = Station.AiPh(name);
            SignalType = Station.SignalType(name);
            // Start values of st_Io_*_AnalogInput, as retained in the PLC
            MinRaw.RetainValue = 0;
            MaxRaw.RetainValue = 27648;
            MinPv.RetainValue = minPv;
            MaxPv.RetainValue = maxPv;
            FilterEmaAlpha.RetainValue = 0.2;
            FilterOutlierMaxJump.RetainValue = 1000.0;
            FilterOutlierMaxRejectCount.RetainValue = 5;
        }

        /// <summary>Retained values as left by the last load of a library entry (a station that has been running).</summary>
        public void RetainFrom(PlcTags t, string libraryEntry, string adjustmentEntry)
        {
            Coefficient.RetainValue = t.R(adjustmentEntry + "Coefficient");
            Offset.RetainValue = t.R(adjustmentEntry + "Offset");
            FilterAverageCount.RetainValue = t.I(adjustmentEntry + "FilterAverageCount");
            MinPv.RetainValue = t.R(libraryEntry + "MinPv");
            MaxPv.RetainValue = t.R(libraryEntry + "MaxPv");
            MinRaw.RetainValue = t.I(libraryEntry + "MinRaw");
            MaxRaw.RetainValue = t.I(libraryEntry + "MaxRaw");
            FilterType.RetainValue = t.I(libraryEntry + "FilterType");
            FilterEmaAlpha.RetainValue = t.R(libraryEntry + "FilterEmaAlpha");
            FilterOutlierMaxJump.RetainValue = t.R(libraryEntry + "FilterOutlierMaxJump");
            FilterOutlierMaxRejectCount.RetainValue = t.I(libraryEntry + "FilterOutlierMaxRejectCount");
        }

        public void Run(PlcTags t, int rawValue)
        {
            MinRaw.Run(t, _hp + "MinRaw", _ph + "MinRaw", 0, true, true);
            MaxRaw.Run(t, _hp + "MaxRaw", _ph + "MaxRaw", 0, true, true);
            MinPv.Run(t, _hp + "MinPv", _ph + "MinPv", 0, true, true);
            MaxPv.Run(t, _hp + "MaxPv", _ph + "MaxPv", 0, true, true);
            FilterType.Run(t, _hp + "FilterType", _ph + "FilterType", 0, true, true);
            FilterEmaAlpha.Run(t, _hp + "FilterEmaAlpha", _ph + "FilterEmaAlpha", 0, true, true);
            FilterOutlierMaxJump.Run(t, _hp + "FilterOutlierMaxJump", _ph + "FilterOutlierMaxJump", 0, true, true);
            FilterOutlierMaxRejectCount.Run(t, _hp + "FilterOutlierMaxRejectCount", _ph + "FilterOutlierMaxRejectCount", 0, true, true);
            Coefficient.Run(t, _hp + "Coefficient", _ph + "Coefficient", 0, true, true);
            Offset.Run(t, _hp + "Offset", _ph + "Offset", 0, true, true);
            FilterAverageCount.Run(t, _hp + "FilterAverageCount", _ph + "FilterAverageCount", 0, true, true);

            // Retained parameters read 0 after a reinitialising download; values that cannot be meant are replaced by the defaults
            if (Coefficient.Value == 0.0) Coefficient.Value = 1.0;
            if (MinRaw.Value == 0 && MaxRaw.Value == 0) MaxRaw.Value = AnalogFilter.DefaultMaxRaw;
            if (FilterEmaAlpha.Value <= 0.0 || FilterEmaAlpha.Value > 1.0) FilterEmaAlpha.Value = AnalogFilter.DefaultEmaAlpha;
            if (FilterOutlierMaxJump.Value <= 0.0) FilterOutlierMaxJump.Value = AnalogFilter.DefaultOutlierMaxJump;
            if (FilterOutlierMaxRejectCount.Value <= 0) FilterOutlierMaxRejectCount.Value = AnalogFilter.DefaultOutlierMaxRejectCount;
            if (FilterAverageCount.Value < 0) FilterAverageCount.Value = 0;
            else if (FilterAverageCount.Value > AnalogFilter.AverageMax) FilterAverageCount.Value = AnalogFilter.AverageMax;

            bool wireBreak = rawValue == RawOverflow || rawValue == RawUnderflow;
            double raw = rawValue;

            // Seed the filters so they do not ramp up from 0 after a restart
            if (_firstCycle)
            {
                _rawWithEma = raw;
                _rawWithEmaOutlier = raw;
                _averageFilled = 0;
                _firstCycle = false;
            }

            // A wire-break reading would poison the filters, so they only follow valid readings
            if (!wireBreak)
            {
                double alpha = FilterEmaAlpha.Value;
                _rawWithEma = alpha * raw + (1.0 - alpha) * _rawWithEma;

                if (Math.Abs(raw - _rawWithEmaOutlier) > FilterOutlierMaxJump.Value)
                {
                    _rejectCount++;
                    if (_rejectCount >= FilterOutlierMaxRejectCount.Value)
                    {
                        _rawWithEmaOutlier = raw;   // accept as new level
                        _rejectCount = 0;
                    }
                }
                else
                {
                    _rawWithEmaOutlier = alpha * raw + (1.0 - alpha) * _rawWithEmaOutlier;
                    _rejectCount = 0;
                }
            }

            double filtered = FilterType.Value == FilterEma ? _rawWithEma
                            : FilterType.Value == FilterEmaOutlier ? _rawWithEmaOutlier
                            : raw;

            // Optional moving average of the filtered value over the last FilterAverageCount values (0 or 1 = off)
            if (!wireBreak)
            {
                _averageIndex = (_averageIndex + 1) % AnalogFilter.AverageMax;
                _averageBuffer[_averageIndex] = filtered;
                if (_averageFilled < AnalogFilter.AverageMax) _averageFilled++;
            }
            int averageCount = Math.Min(FilterAverageCount.Value, _averageFilled);
            if (FilterAverageCount.Value >= 2 && averageCount >= 1 && !wireBreak)
            {
                double sum = 0.0;
                int row = _averageIndex;
                for (int i = 0; i < averageCount; i++)
                {
                    sum += _averageBuffer[row];
                    row = row == 0 ? AnalogFilter.AverageMax - 1 : row - 1;
                }
                filtered = sum / averageCount;
            }

            double span = MaxRaw.Value - MinRaw.Value;
            double norm = span == 0 ? 0 : (filtered - MinRaw.Value) / span;
            double rawNorm = span == 0 ? 0 : (raw - MinRaw.Value) / span;
            Pv = (MinPv.Value + norm * (MaxPv.Value - MinPv.Value)) * Coefficient.Value + Offset.Value;

            // MinRaw / MaxRaw are the two ends of the signal range
            var (signalMin, signalMax) = AnalogSignal.Range(SignalType);

            t.W(_ph + "RawValue", rawValue);
            t.W(_ph + "FilteredRaw", filtered);
            t.W(_ph + "SignalType", SignalType);
            t.W(_ph + "RawSignal", signalMin + rawNorm * (signalMax - signalMin));
            t.W(_ph + "FilteredSignal", signalMin + norm * (signalMax - signalMin));
            t.W(_ph + "Pv", Pv);
            t.W(_ph + "WireBreak", wireBreak);
        }
    }

    /// <summary>Port of FB_Station_InstrumentPosition with FB_Station_InstrumentPositionHmi.</summary>
    public sealed class InstrumentPosition
    {
        private const int KeyFactor = 1000;
        private static readonly int PositionCount = Station.Positions.Length;   // POSITION_COUNT (25)

        private readonly InstrumentPositionInfo _info;
        private readonly string _hp, _ph;
        public readonly HmiParameterInt Device = new HmiParameterInt();
        private int _mountedDevice;
        private bool _selectionRejected;

        /// <summary>The mounted device, 0 none; its library entry is applied to the channel every cycle.</summary>
        public int MountedDevice => _mountedDevice;
        public InstrumentPositionInfo Info => _info;

        public InstrumentPosition(InstrumentPositionInfo info, int retainedDevice)
        {
            _info = info;
            _hp = Station.PositionHp(info.Name);
            _ph = Station.PositionPh(info.Name);
            Device.RetainValue = retainedDevice;
        }

        public void Run(PlcTags t, int deviceCount, int[] assigned)
        {
            // The device can be changed at any time, also while the zone runs
            Device.Run(t, _hp + "Device", _ph + "Device", 0, true, true);

            // A physical device can be mounted at one position only. A rejected value is written back
            // into the parameter, which pushes it to the HMI again.
            int candidate = Device.Value;
            if (candidate != _mountedDevice)
            {
                _selectionRejected = candidate < 0 || candidate > deviceCount;
                if (!_selectionRejected && candidate > 0)
                {
                    int key = _info.Category * KeyFactor + candidate;
                    for (int i = 1; i <= PositionCount; i++)
                        if (i != _info.Index && assigned[i] == key) _selectionRejected = true;
                }

                if (_selectionRejected) Device.Value = _mountedDevice;
                else _mountedDevice = candidate;
            }

            assigned[_info.Index] = _mountedDevice > 0 ? _info.Category * KeyFactor + _mountedDevice : 0;
            t.W(_ph + "SelectionRejected", _selectionRejected);
        }
    }

    /// <summary>Port of FB_Station_Instruments with FC_Station_LoadInstrument.</summary>
    public sealed class StationInstruments
    {
        private readonly int[] _assigned = new int[Station.Positions.Length + 1];
        public readonly Dictionary<string, InstrumentPosition> Positions = new Dictionary<string, InstrumentPosition>();

        public StationInstruments(Func<InstrumentPositionInfo, int> retainedDevice)
        {
            foreach (var info in Station.Positions)
                Positions[info.Name] = new InstrumentPosition(info, retainedDevice(info));
        }

        public int Device(string position) => Positions[position].MountedDevice;

        /// <summary>
        /// Positions can be re-assigned at any time, also while their zone runs; the mounted device's library entry is
        /// copied into its channel every cycle, so a new device or an edited entry applies at once.
        /// </summary>
        public void Run(PlcTags t)
        {
            foreach (var info in Station.Positions)
            {
                var position = Positions[info.Name];
                position.Run(t, Station.LibrarySize, _assigned);
                if (position.MountedDevice > 0) LoadInstrument(t, info.Category, position.MountedDevice, info.Channel);
            }
        }

        // FC_Station_LoadInstrument: library entry and the device's adjustments (coefficient, offset, moving average) → the
        // channel's HMI-to-PLC parameters. A parameter takes a value over only when it changes. Only the signal type belongs
        // to the channel.
        private static void LoadInstrument(PlcTags t, int category, int device, string channel)
        {
            string entry = Station.LibraryEntry(category, device), adjustment = Station.AdjustmentEntry(category, device), hp = Station.AiHp(channel);
            foreach (var n in Station.AiChannelRealParameters) t.W(hp + n + ".Value", t.R(adjustment + n));
            foreach (var n in Station.AiChannelIntParameters) t.W(hp + n + ".Value", t.I(adjustment + n));
            foreach (var n in Station.AiRealParameters) t.W(hp + n + ".Value", t.R(entry + n));
            foreach (var n in Station.AiIntParameters) t.W(hp + n + ".Value", t.I(entry + n));
        }
    }
}
