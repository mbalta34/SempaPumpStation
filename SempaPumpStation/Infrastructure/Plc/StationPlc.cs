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
        private readonly Dictionary<string, int> _ioLink = new Dictionary<string, int>();
        private readonly Dictionary<string, DigitalOutputChannel> _outputs =
            DigitalOutputs.All.ToDictionary(o => o.Name, o => new DigitalOutputChannel(o));

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

            // ================================================================ IO-Link → "Io"
            // The simulated field drives the hub port each sensor is wired to (its IO list port); FB_Station_Instruments
            // routes every port into its channel.
            WriteIoLink();
            foreach (var ch in _ai.Values) ch.Run(_t, RoutedRaw(ch.Name));

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

            WriteDigitalInputs();

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

            // "Io".DigitalOutputs: every command goes through its output channel (FB_Io_DigitalOutput), so a forced
            // output is what reaches the field
            var fieldOutputs = new ZoneOutputs[4];
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var o = fieldOutputs[z] = _zoneOutputs[z].Clone();
                string zone = "Zone" + z + "_";
                foreach (var s in Station.Sources)
                {
                    if (!s.IsMagneticSwitch)
                        o.ContactorClose[s.Id] = Output(zone + s.Device + "_Close", o.ContactorClose[s.Id]);
                    else
                    {
                        o.SwitchOpen[s.Id] = Output(zone + s.Device + "_Open", o.SwitchOpen[s.Id]);
                        o.SwitchClose[s.Id] = Output(zone + s.Device + "_Close", o.SwitchClose[s.Id]);
                        o.SwitchChargeSpring[s.Id] = Output(zone + s.Device + "_ChargeSpring", o.SwitchChargeSpring[s.Id]);
                    }
                }
                o.LampGreen = Output(zone + "LampGreen", o.LampGreen);
                o.LampYellow = Output(zone + "LampYellow", o.LampYellow);
                o.LampRed = Output(zone + "LampRed", o.LampRed);
            }
            foreach (var s in Station.Sources) run[s.Id] = Output("Drive_" + s.Drive + "_Run", run[s.Id]);

            _field.Update(fieldOutputs, run, speedReference, _instruments.Device, _cycle);
        }

        private bool Output(string channel, bool cmd) => _outputs[channel].Run(_t, cmd);

        // "Io".DigitalInputs as the field drives them (not forced in the simulation), for the IO watch table
        private void WriteDigitalInputs()
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                string zone = "Zone" + z + "_";
                foreach (var s in Station.Sources)
                {
                    Input(zone + s.Device + "_Open", _field.DeviceOpen(z, s.Id));
                    Input(zone + s.Device + "_Closed", _field.DeviceClosed(z, s.Id));
                    if (s.IsMagneticSwitch) Input(zone + s.Device + "_SpringCharged", _field.SpringCharged(z, s.Id));
                }
                Input(zone + "PendantStart", _field.PendantStart(z));
                Input(zone + "PendantStop", _field.PendantStop(z));
                Input(zone + "PendantTakeSample", _field.PendantTakeSample(z));
                Input(zone + "EStopOk", _field.EStopOk(z));
            }
            foreach (var s in Station.Sources)
            {
                Input("Drive_" + s.Drive + "_Running", _field.DriveRunning(s.Id));
                Input("Drive_" + s.Drive + "_Fault", _field.DriveFault(s.Id));
            }
        }

        private void Input(string channel, bool value)
        {
            string ph = "MaintenancePh.Io.DigitalInputs." + channel + ".";
            _t.W(ph + "RawValue", value);
            _t.W(ph + "Pv", value);
        }

        // The physical wiring: every sensor on its IO list port, free ports read as an open 4-20 mA input
        private void WriteIoLink()
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
                foreach (var hub in new[] { IoLinkModule.RtdHub, IoLinkModule.AnalogHub })
                    for (int p = 0; p < IoLinkLayout.PortCount; p++)
                        _ioLink[IoLinkLayout.Raw(z, hub, p)] = IoLinkLayout.OpenInputRaw;

            foreach (var p in Station.Positions)
            {
                var w = IoLinkLayout.Wiring(p);
                _ioLink[IoLinkLayout.Raw(w.Zone, w.Hub, w.Port)] = _field.Raw(p.Channel);
            }
            foreach (var f in IoLinkLayout.FixedInputs())
                _ioLink[f.Field != null ? IoLinkLayout.Field(f.Zone, f.Field) : IoLinkLayout.Raw(f.Zone, f.Hub, f.Port)] = _field.Raw(f.Channel);

            foreach (var kv in _ioLink) _t.W(kv.Key, kv.Value);
        }

        // FB_Station_Instruments' routing: every channel from its port in the IO list
        private int RoutedRaw(string channel)
        {
            var position = Station.Position(channel);
            if (position != null)
            {
                var w = IoLinkLayout.Wiring(position);
                return _t.I(IoLinkLayout.Raw(w.Zone, w.Hub, w.Port));
            }
            var f = IoLinkLayout.FixedInputs().First(x => x.Channel == channel);
            return _t.I(f.Field != null ? IoLinkLayout.Field(f.Zone, f.Field) : IoLinkLayout.Raw(f.Zone, f.Hub, f.Port));
        }
    }
}
