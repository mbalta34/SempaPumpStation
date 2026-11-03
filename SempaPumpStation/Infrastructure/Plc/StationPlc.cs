using System;
using System.Collections.Generic;
using System.Linq;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// The simulated MainPLC: OB1 calls "Io" (the analog input channels) and then FB_Station_Main,
    /// whose port is below. One <see cref="Scan"/> is one PLC cycle followed by one field update.
    /// </summary>
    public sealed class StationPlc
    {
        private readonly PlcTags _t;
        private readonly FieldSimulation _field;
        private readonly PlcCycle _cycle = new PlcCycle();
        private readonly Dictionary<string, AnalogInputChannel> _ai = new Dictionary<string, AnalogInputChannel>();
        private readonly StationZone[] _zones = new StationZone[4];
        private readonly ZoneInputs[] _zoneInputs = new ZoneInputs[4];
        private readonly ZoneOutputs[] _zoneOutputs = new ZoneOutputs[4];
        private readonly StationInstruments _instruments;

        public FieldSimulation Field => _field;

        public StationPlc(PlcTags tags, Func<InstrumentPositionInfo, int> retainedDevice)
        {
            _t = tags;
            _field = new FieldSimulation(tags);
            _instruments = new StationInstruments(retainedDevice);

            foreach (var ch in Station.AnalogInputs()) _ai[ch] = new AnalogInputChannel(ch);

            // FB_Hmi_Parameter* writes its retained value back on its second scan, which overwrites the load
            // FB_Station_Instruments does on the first scan; so start from what the last load left retained.
            foreach (var p in Station.Positions)
            {
                int device = retainedDevice(p);
                if (device > 0) _ai[p.Channel].RetainFrom(tags, Station.LibraryEntry(p.Category, device));
            }

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                // The zone sensors are fixed instruments; their channels were set up at commissioning
                SensorRange("Zone" + z + "_WaterTemperature", 0, 100);
                SensorRange("Zone" + z + "_AmbientTemperature", 0, 100);
                SensorRange("Zone" + z + "_Vibration", 0, 25);
                SensorRange("Zone" + z + "_Temperature", 0, 150);
                SensorRange("Zone" + z + "_Sound", 30, 130);
                SensorRange("Zone" + z + "_Speed", 0, 3600);

                _zones[z] = new StationZone(z);
                _zoneInputs[z] = new ZoneInputs();
                _zoneOutputs[z] = _zones[z].Outputs;
            }
        }

        private void SensorRange(string channel, double minPv, double maxPv) => _ai[channel] = new AnalogInputChannel(channel, minPv, maxPv);

        public void Scan(double dtMs)
        {
            _cycle.DtMs = dtMs;
            _cycle.TimeMs += dtMs;

            // ================================================================ "Io"
            foreach (var ch in _ai.Values) ch.Run(_t, _field.Raw(ch.Name));

            // ================================================================ read IO
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var p = _zoneInputs[z];
                for (int s = 1; s <= Station.SourceCount; s++)
                {
                    p.DeviceOpen[s] = _field.DeviceOpen(z, s);
                    p.DeviceClosed[s] = _field.DeviceClosed(z, s);
                }
                p.SpringCharged[7] = _field.SpringCharged(z, 7);
                p.SpringCharged[8] = _field.SpringCharged(z, 8);
                p.PendantStart = _field.PendantStart(z);
                p.PendantStop = _field.PendantStop(z);
                p.PendantTakeSample = _field.PendantTakeSample(z);
                p.EStopOk = _field.EStopOk(z);

                var fm = Station.FlowmeterPositions(z);
                for (int f = 1; f <= 5; f++) p.Flowmeters[f] = f <= fm.Length ? _ai[fm[f - 1]].Pv : 0.0;
                for (int t = 1; t <= Station.TransmitterCount; t++) p.DischargeTransmitters[t] = _ai[Station.TransmitterPosition(z, t)].Pv;
                p.Vacuummeter = _ai[Station.VacuummeterPosition(z)].Pv;
                p.WaterTemperature = _ai["Zone" + z + "_WaterTemperature"].Pv;
                p.AmbientTemperature = _ai["Zone" + z + "_AmbientTemperature"].Pv;
                p.Vibration = _ai["Zone" + z + "_Vibration"].Pv;
                p.Temperature = _ai["Zone" + z + "_Temperature"].Pv;
                p.Sound = _ai["Zone" + z + "_Sound"].Pv;
                p.Speed = _ai["Zone" + z + "_Speed"].Pv;
                p.Voltage = 0.0;   // TODO Modbus power meter
                p.Current = 0.0;   // TODO Modbus power meter

                int selected = _zoneOutputs[z].SelectedSource;
                p.DriveRunning = selected > 0 && _field.DriveRunning(selected);
                p.DriveFault = selected > 0 && _field.DriveFault(selected);
            }

            // ================================================================ instruments
            // A zone counts as running from its run request until its drive has stopped
            var zoneRunning = new bool[4];
            for (int z = 1; z <= Station.ZoneCount; z++)
                zoneRunning[z] = _zoneOutputs[z].RunRequest || _zoneInputs[z].DriveRunning;
            _instruments.Run(_t, zoneRunning);

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var p = _zoneInputs[z];
                var fm = Station.FlowmeterPositions(z);
                for (int f = 1; f <= 5; f++) p.FlowmeterDevices[f] = f <= fm.Length ? _instruments.Device(fm[f - 1]) : 0;
                for (int t = 1; t <= Station.TransmitterCount; t++) p.DischargeTransmitterDevices[t] = _instruments.Device(Station.TransmitterPosition(z, t));
                p.VacuummeterDevice = _instruments.Device(Station.VacuummeterPosition(z));
            }

            // ================================================================ zones
            // A source is held by another zone when that zone has selected it or its device for it is not open.
            // Each zone is evaluated with the latest selections of the zones before it.
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var sourceHeld = new bool[9];
                var others = Enumerable.Range(1, Station.ZoneCount).Where(o => o != z).ToArray();
                for (int i = 1; i <= Station.SourceCount; i++)
                    foreach (int o in others)
                        sourceHeld[i] |= _zoneOutputs[o].SelectedSource == i
                                         || !(_zoneInputs[o].DeviceOpen[i] && !_zoneInputs[o].DeviceClosed[i]);

                // Zone 1 and zone 2 share their flowmeters
                var flowmeterHeld = new bool[6];
                if (z <= 2)
                    for (int i = 1; i <= Station.SharedFlowmeterCount; i++)
                        flowmeterHeld[i] = _zoneOutputs[3 - z].SelectedFlowmeter == i;

                _zones[z].Run(_t, _zoneInputs[z], sourceHeld, flowmeterHeld, Station.FlowmeterCount(z), _cycle);
            }

            // ================================================================ write IO
            // Drives: a source feeds at most one zone, so the owning zone's request and speed reference go to it
            var run = new bool[9];
            var speedReference = new double[9];
            for (int i = 1; i <= Station.SourceCount; i++)
                for (int z = 1; z <= Station.ZoneCount; z++)
                    if (_zoneOutputs[z].SelectedSource == i)
                    {
                        run[i] = _zoneOutputs[z].RunRequest;
                        speedReference[i] = _zoneOutputs[z].SpeedReference;
                    }

            _field.Update(_zoneOutputs, run, speedReference, _instruments.Device, _cycle);
        }
    }
}
