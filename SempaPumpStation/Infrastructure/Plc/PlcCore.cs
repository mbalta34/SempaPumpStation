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
    /// Port of FB_Hmi_ParameterInt (RailBase 1.9.2, no min/max limits). <see cref="Value"/> is the PLC's working value. The
    /// PLC never writes Hp: an HMI entry is a change of Hp's EntryCounter (the HMI counts every entry, 1 … 255, never 0), so
    /// an entry that repeats the value counts too; 0 only re-syncs (the Hp block after a restart). Whatever counter Hp
    /// holds at start-up is not an entry. A change of <see cref="Value"/> by PLC code sets Q.
    /// </summary>
    public sealed class HmiParameterInt
    {
        private bool _firstCycle = true;
        private int _previousValue, _lastEntryCounter;
        public int Value;
        public int RetainValue;
        public bool Q { get; private set; }

        public void Run(PlcTags t, string hp, string ph, int initialValue, bool retain, bool enable)
        {
            Q = false;
            if (_firstCycle)
            {
                Value = retain ? RetainValue : initialValue;
                _lastEntryCounter = t.I(hp + ".EntryCounter");
                _firstCycle = false;
                return;
            }

            if (_previousValue != Value)
            {
                _previousValue = Value;
                Q = true;
            }

            if (HmiEntry.Taken(t, hp, ref _lastEntryCounter) && enable)
            {
                Value = t.I(hp + ".Value");
                if (_previousValue != Value)
                {
                    _previousValue = Value;
                    Q = true;
                }
            }

            RetainValue = Value;
            t.W(ph + ".Value", Value);
            t.W(ph + ".Enabled", enable);
        }
    }

    /// <summary>Port of FB_Hmi_ParameterReal (RailBase 1.9.2, no min/max limits). Same handshake as the Int version.</summary>
    public sealed class HmiParameterReal
    {
        private bool _firstCycle = true;
        private double _previousValue;
        private int _lastEntryCounter;
        public double Value;
        public double RetainValue;
        public bool Q { get; private set; }

        public void Run(PlcTags t, string hp, string ph, double initialValue, bool retain, bool enable)
        {
            Q = false;
            if (_firstCycle)
            {
                Value = retain ? RetainValue : initialValue;
                _lastEntryCounter = t.I(hp + ".EntryCounter");
                _firstCycle = false;
                return;
            }

            if (!Same(_previousValue, Value))
            {
                _previousValue = Value;
                Q = true;
            }

            if (HmiEntry.Taken(t, hp, ref _lastEntryCounter) && enable)
            {
                Value = t.R(hp + ".Value");
                if (!Same(_previousValue, Value))
                {
                    _previousValue = Value;
                    Q = true;
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
    /// Port of FB_Hmi_CommandButton (RailBase 1.9.2) with buttonType BUTTON_MOMENTARY: a press is a change of Hp's
    /// EntryCounter (Click itself is not looked at); the counter Hp holds at start-up is no press, 0 only re-syncs.
    /// </summary>
    public sealed class HmiCommandButton
    {
        private bool _firstCycle = true;
        private bool _value, _previousValue;
        private int _lastEntryCounter;
        public bool QR { get; private set; }

        public void Run(PlcTags t, string hp, string ph, bool enable = true)
        {
            QR = false;
            if (_value || _firstCycle) _value = false;
            if (_firstCycle)
            {
                _lastEntryCounter = t.I(hp + ".EntryCounter");
                _firstCycle = false;
            }

            if (HmiEntry.Taken(t, hp, ref _lastEntryCounter) && enable) _value = true;

            if (_previousValue != _value)
            {
                _previousValue = _value;
                QR = _value;
            }

            t.W(ph + ".Value", _value);
            t.W(ph + ".Enabled", enable);
        }
    }

    /// <summary>The entry-counter handshake of FB_Hmi_* (RailBase 1.9.2).</summary>
    public static class HmiEntry
    {
        /// <summary>Whether Hp's EntryCounter shows a new entry since the last scan; 0 is never an entry, it only re-syncs.</summary>
        public static bool Taken(PlcTags t, string hp, ref int lastEntryCounter)
        {
            int counter = t.I(hp + ".EntryCounter");
            if (counter == 0) { lastEntryCounter = 0; return false; }
            if (counter == lastEntryCounter) return false;
            lastEntryCounter = counter;
            return true;
        }

        /// <summary>The next count, as the HMI counts (SiemensS7PlcTag.NextEntryCount): 1 … 255, then 1 again.</summary>
        public static int Next(int current) => current >= 255 || current < 0 ? 1 : current + 1;
    }
}
