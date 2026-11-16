using System;
using System.Collections.Generic;
using TerakkiFramework.Plc;
using TerakkiFramework.Plc.Abstractions;
using TerakkiFramework.Plc.Local;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// The simulated PLC's view of the tag store. Reads see whatever the HMI or the PLC last wrote;
    /// writes go in the device direction and only notify bindings when the value really changes,
    /// so a 100 ms scan does not churn the UI.
    /// </summary>
    public sealed class PlcTags
    {
        private readonly Dictionary<string, IPlcTag> _tags;

        public PlcTags(Dictionary<string, IPlcTag> tags) { _tags = tags; }

        public IPlcTag Tag(string name)
        {
            IPlcTag t;
            if (!_tags.TryGetValue(name, out t)) throw new KeyNotFoundException("Tag not in catalogue: " + name);
            return t;
        }

        public bool B(string name)
        {
            var v = Tag(name).TagValue;
            if (v is bool b) return b;
            try { return v != null && Convert.ToBoolean(v); } catch { return false; }
        }

        public int I(string name)
        {
            var v = Tag(name).TagValue;
            try { return v == null ? 0 : (int)Math.Round(Convert.ToDouble(v)); } catch { return 0; }
        }

        public double R(string name)
        {
            var v = Tag(name).TagValue;
            try { return v == null ? 0 : Convert.ToDouble(v); } catch { return 0; }
        }

        public string S(string name) => Tag(name).TagValue?.ToString() ?? string.Empty;

        /// <summary>Write a value as the PLC (device push), converted to the tag's data type.</summary>
        public void W(string name, object value)
        {
            var tag = Tag(name);
            object typed = ToType(tag.DataType, value);
            if (Equals(tag.TagValue, typed)) return;
            (tag as LocalPlcTag)?.SetFromDevice(typed);
        }

        private static object ToType(DataType type, object value)
        {
            switch (type)
            {
                case DataType.BOOL:
                case DataType.MOMENTARY_BOOL: return Convert.ToBoolean(value);
                case DataType.INT: return (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(Convert.ToDouble(value))));
                case DataType.DINT: return (int)Math.Round(Convert.ToDouble(value));
                case DataType.REAL: return (float)Convert.ToDouble(value);
                case DataType.LREAL: return Convert.ToDouble(value);
                default: return value?.ToString() ?? string.Empty;
            }
        }
    }

    /// <summary>Scan context: the cycle time and the station-wide clocks of "Utils".dataClock.</summary>
    public sealed class PlcCycle
    {
        public double DtMs;
        public double TimeMs;
        public bool Flash1s => (TimeMs % 1000.0) < 500.0;   // 1 Hz, 50 % duty
    }

    public sealed class Ton
    {
        private double _elapsed;
        public bool Q { get; private set; }

        public void Run(bool input, double ptMs, PlcCycle c)
        {
            if (!input) { _elapsed = 0; Q = false; return; }
            _elapsed = Math.Min(ptMs, _elapsed + c.DtMs);
            Q = _elapsed >= ptMs;
        }
    }

    /// <summary>TP: a rising edge starts a pulse of fixed length; the input is ignored while it runs.</summary>
    public sealed class Tp
    {
        private double _elapsed;
        private bool _running, _previousInput;
        public bool Q { get; private set; }

        public void Run(bool input, double ptMs, PlcCycle c)
        {
            if (_running)
            {
                _elapsed += c.DtMs;
                if (_elapsed >= ptMs) _running = false;
            }
            else if (input && !_previousInput)
            {
                _running = true;
                _elapsed = 0;
            }
            _previousInput = input;
            Q = _running;
        }
    }

    public sealed class RTrig
    {
        private bool _previous;
        public bool Q { get; private set; }

        public void Run(bool clk)
        {
            Q = clk && !_previous;
            _previous = clk;
        }
    }

    /// <summary>
    /// Port of BluePrism's FB_Hmi_ParameterInt (no min/max limits). <see cref="Value"/> is the PLC's working
    /// value. The PLC never writes Hp: an HMI entry is a change of Hp's value since the last scan, so whatever
    /// Hp holds at start-up is not an entry. A change of <see cref="Value"/> by PLC code sets Q.
    /// </summary>
    public sealed class HmiParameterInt
    {
        private bool _firstCycle = true;
        private int _previousValue, _toPlcOldValue;
        public int Value;
        public int RetainValue;
        public bool Q { get; private set; }

        public void Run(PlcTags t, string hp, string ph, int initialValue, bool retain, bool enable)
        {
            Q = false;
            if (_firstCycle)
            {
                Value = retain ? RetainValue : initialValue;
                _toPlcOldValue = t.I(hp + ".Value");
                _firstCycle = false;
                return;
            }

            if (_previousValue != Value)
            {
                _previousValue = Value;
                Q = true;
            }

            int requested = t.I(hp + ".Value");
            if (requested != _toPlcOldValue)
            {
                _toPlcOldValue = requested;
                if (enable)
                {
                    Value = requested;
                    if (_previousValue != Value)
                    {
                        _previousValue = Value;
                        Q = true;
                    }
                }
            }

            RetainValue = Value;
            t.W(ph + ".Value", Value);
            t.W(ph + ".Enabled", enable);
        }
    }

    /// <summary>Port of BluePrism's FB_Hmi_ParameterReal (no min/max limits). Same handshake as the Int version.</summary>
    public sealed class HmiParameterReal
    {
        private bool _firstCycle = true;
        private double _previousValue, _toPlcOldValue;
        public double Value;
        public double RetainValue;
        public bool Q { get; private set; }

        public void Run(PlcTags t, string hp, string ph, double initialValue, bool retain, bool enable)
        {
            Q = false;
            if (_firstCycle)
            {
                Value = retain ? RetainValue : initialValue;
                _toPlcOldValue = t.R(hp + ".Value");
                _firstCycle = false;
                return;
            }

            if (!Same(_previousValue, Value))
            {
                _previousValue = Value;
                Q = true;
            }

            double requested = t.R(hp + ".Value");
            if (!Same(requested, _toPlcOldValue))
            {
                _toPlcOldValue = requested;
                if (enable)
                {
                    Value = requested;
                    if (!Same(_previousValue, Value))
                    {
                        _previousValue = Value;
                        Q = true;
                    }
                }
            }

            RetainValue = Value;
            t.W(ph + ".Value", Value);
            t.W(ph + ".Enabled", enable);
        }

        // The tags hold Real (float), so compare at float precision
        private static bool Same(double a, double b) => (float)a == (float)b;
    }

    /// <summary>
    /// Port of BluePrism's FB_Hmi_CommandButton with buttonType BUTTON_MOMENTARY: a press is the rising edge
    /// of Hp.Click, which the HMI pulses; the PLC never clears it. A Click already TRUE at start-up is no press.
    /// </summary>
    public sealed class HmiCommandButton
    {
        private bool _firstCycle = true;
        private bool _value, _previousValue, _clickLastScan;
        public bool QR { get; private set; }

        public void Run(PlcTags t, string hp, string ph, bool enable = true)
        {
            QR = false;
            bool click = t.B(hp + ".Click");
            if (_firstCycle) _clickLastScan = click;

            if (_value || _firstCycle)
            {
                _value = false;
                _firstCycle = false;
            }

            if (enable && click && !_clickLastScan) _value = true;
            _clickLastScan = click;

            if (_previousValue != _value)
            {
                _previousValue = _value;
                QR = _value;
            }

            t.W(ph + ".Value", _value);
            t.W(ph + ".Enabled", enable);
        }
    }
}
