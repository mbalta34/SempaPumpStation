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
        private bool _safetyStop;   // a power device was manipulated: every drive is stopped until the zone resets it
        private readonly Dictionary<string, int> _ioLink = new Dictionary<string, int>();
        private readonly Dictionary<string, DigitalOutputChannel> _outputs =
            DigitalOutputs.All.ToDictionary(o => o.Name, o => new DigitalOutputChannel(o));
        // "Io".DigitalInputs: every input, and the value the program sees this scan (forced or from the field)
        private readonly Dictionary<string, DigitalInputChannel> _inputs =
            DigitalInputs.All.ToDictionary(i => i.Name, i => new DigitalInputChannel(i));
        private readonly Dictionary<string, bool> _di = new Dictionary<string, bool>();
        private bool Di(string channel) => _di[channel];

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
                if (device > 0) _ai[p.Channel].RetainFrom(tags, Station.LibraryEntry(p.Category, device), Station.AdjustmentEntry(p.Category, device));
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
                SensorRange("Zone" + z + "_Torque", 0, 1000);

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

            // ================================================================ "Communication": PM5550s → "Powermeters"
            WritePowermeters(dtMs / 1000.0);
            WriteDrive();

            // FB_Station_Main: the program version for the HMI (the simulation is the program this HMI is built for)
            _t.W("StationPh.PlcVersion", AppVersion.ExpectedPlcVersion);

            // ================================================================ read IO
            // FB_Io_DigitalInputs first: the program works with each input's pv, which may be forced
            ReadDigitalInputs();
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var p = _zoneInputs[z];
                string zone = "Zone" + z + "_";
                for (int s = 1; s <= Station.SourceCount; s++)
                {
                    p.DeviceOpen[s] = Di(zone + Station.Source(s).Device + "_Open");
                    p.DeviceClosed[s] = Di(zone + Station.Source(s).Device + "_Closed");
                }
                p.SpringCharged[7] = Di(zone + Station.Source(7).Device + "_SpringCharged");
                p.SpringCharged[8] = Di(zone + Station.Source(8).Device + "_SpringCharged");
                p.PendantStart = Di(zone + "PendantStart");
                p.PendantStop = Di(zone + "PendantStop");
                p.PendantTakeSample = Di(zone + "PendantTakeSample");
                p.EStopOk = Di(zone + "EStopOk");
                p.PanelEStopOk = Di(zone + "PanelEStopOk");

                var fm = Station.FlowmeterPositions(z);
                for (int f = 1; f <= 5; f++) p.Flowmeters[f] = f <= fm.Length ? _ai[fm[f - 1]].Pv : 0.0;
                for (int t = 1; t <= Station.MaxTransmitterCount; t++)
                    p.DischargeTransmitters[t] = t <= Station.TransmitterCount(z) ? _ai[Station.TransmitterPosition(z, t)].Pv : 0.0;
                for (int v = 1; v <= Station.MaxVacuummeterCount; v++)
                    p.Vacuummeters[v] = v <= Station.VacuummeterCount(z) ? _ai[Station.VacuummeterPosition(z, v)].Pv : 0.0;
                p.WaterTemperature = _ai["Zone" + z + "_WaterTemperature"].Pv;
                p.AmbientTemperature = _ai["Zone" + z + "_AmbientTemperature"].Pv;
                p.Vibration = _ai["Zone" + z + "_Vibration"].Pv;
                p.Temperature = _ai["Zone" + z + "_Temperature"].Pv;
                p.Sound = _ai["Zone" + z + "_Sound"].Pv;
                p.Speed = _ai["Zone" + z + "_Speed"].Pv;
                p.Torque = _ai["Zone" + z + "_Torque"].Pv;
                int selected = _zoneOutputs[z].SelectedSource;
                p.DriveRunning = selected > 0 && Di("Drive_" + Station.Source(selected).Drive + "_Running");
                p.DriveFault = selected > 0 && Di("Drive_" + Station.Source(selected).Drive + "_Fault");
                // As FB_Station_Main: the meter of the selected source, on the 630 kW VFD the drive's motor side
                // (FC_Station_DriveElectrical); 0 while none is selected
                var e = PumpCalc.ZoneElectrical(_t.R, _t.B, selected);
                p.Voltage = e?.Voltage ?? 0.0;
                p.Current = e?.Current ?? 0.0;
                p.PowerFactor = e?.PowerFactor ?? 0.0;
            }

            // ================================================================ instruments
            // Positions can be re-assigned and library entries edited at any time; they apply at once
            _instruments.Run(_t);

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var p = _zoneInputs[z];
                var fm = Station.FlowmeterPositions(z);
                for (int f = 1; f <= 5; f++) p.FlowmeterDevices[f] = f <= fm.Length ? _instruments.Device(fm[f - 1]) : 0;
                for (int t = 1; t <= Station.MaxTransmitterCount; t++)
                    p.DischargeTransmitterDevices[t] = t <= Station.TransmitterCount(z) ? _instruments.Device(Station.TransmitterPosition(z, t)) : 0;
                for (int v = 1; v <= Station.MaxVacuummeterCount; v++)
                    p.VacuummeterDevices[v] = v <= Station.VacuummeterCount(z) ? _instruments.Device(Station.VacuummeterPosition(z, v)) : 0;
            }

            // A manipulation in any zone (found last scan) holds every zone stopped
            for (int z = 1; z <= Station.ZoneCount; z++) _zoneInputs[z].SafetyStop = _safetyStop;

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

                // Every zone has its own flowmeters, so none is held by another zone
                var flowmeterHeld = new bool[6];

                _zones[z].Run(_t, _zoneInputs[z], sourceHeld, flowmeterHeld, Station.FlowmeterCount(z),
                              Station.TransmitterCount(z), Station.VacuummeterCount(z), _cycle);
            }

            // A contactor or magnetic switch changed state without a command: stop every motor in this scan
            _safetyStop = false;
            for (int z = 1; z <= Station.ZoneCount; z++) _safetyStop |= _zoneOutputs[z].Manipulated;

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
            if (_safetyStop)
                for (int i = 1; i <= Station.SourceCount; i++) run[i] = false;

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
                o.LampStart = Output(zone + "LampStart", o.LampStart);
                o.LampStop = Output(zone + "LampStop", o.LampStop);
                o.LampTakeSample = Output(zone + "LampTakeSample", o.LampTakeSample);
                o.LampEStop = Output(zone + "LampEStop", o.LampEStop);
                o.LampPanelEStop = Output(zone + "LampPanelEStop", o.LampPanelEStop);
            }
            foreach (var s in Station.Sources) run[s.Id] = Output("Drive_" + s.Drive + "_Run", run[s.Id]);

            // "Io".AnalogOutputs: the VFD speed references, scaled 0..100 % → 0..27648 as commissioned
            foreach (var o in AnalogOutputList.All)
            {
                double percent = speedReference[o.Source.Id];
                _t.W(o.Ph + "Pv", percent);
                _t.W(o.Ph + "RawValue", (int)System.Math.Round(System.Math.Max(0, System.Math.Min(100, percent)) / 100.0 * 27648));
                _t.W(o.Ph + "MinPv.Value", 0.0);
                _t.W(o.Ph + "MaxPv.Value", 100.0);
            }

            _field.Update(fieldOutputs, run, speedReference, _instruments.Device, _cycle);
        }

        private bool Output(string channel, bool cmd) => _outputs[channel].Run(_t, cmd);

        // "Io".DigitalInputs as the field drives them (not forced in the simulation), for the IO watch table
        private void ReadDigitalInputs()
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
                Input(zone + "PanelEStopOk", _field.PanelEStopOk(z));
            }
            foreach (var s in Station.Sources)
            {
                Input("Drive_" + s.Drive + "_Running", _field.DriveRunning(s.Id) && !_t.B(Station.SimDrive(s.Id) + "NoFeedback"));
                Input("Drive_" + s.Drive + "_Fault", _field.DriveFault(s.Id));
            }
        }

        private void Input(string channel, bool value) => _di[channel] = _inputs[channel].Run(_t, value);

        // FB_Communication_Pm5550Decoder's result for a 3-wire system (PM5550 set to 3PH3W): every meter answers;
        // the phases carry the line-to-line voltages and the phase currents, the powers and the power factor are totals only.
        // Energies count up in Wh, as the PM5550 reports them. FB_Communication_Main then filters each meter
        // (FB_Communication_PowermeterFilter, a value every 100 ms) into "Powermeters".
        private readonly double[] _activeWh = new double[9], _reactiveWh = new double[9], _apparentWh = new double[9];
        private readonly PowermeterFilter[] _meterFilters =
            Enumerable.Range(0, Station.SourceCount + 1).Select(s => s == 0 ? null : new PowermeterFilter(s)).ToArray();
        private double _meterSampleMs;
        private bool _meterSample;   // this scan has the 100 ms pulse (meters and drive)
        private const double MeterSampleMs = 100.0;

        private void WritePowermeters(double dt)
        {
            // A pulse every 100 ms (SampleTimer)
            _meterSampleMs += dt * 1000.0;
            bool sample = _meterSample = _meterSampleMs >= MeterSampleMs;
            if (sample) _meterSampleMs = 0.0;

            for (int s = 1; s <= Station.SourceCount; s++)
            {
                var m = _field.Meter(s);
                string meter = Station.Meter(s);
                _activeWh[s] += m.ActiveKw * dt / 3.6;
                _reactiveWh[s] += m.ReactiveKvar * dt / 3.6;
                _apparentWh[s] += m.ApparentKva * dt / 3.6;

                // Decoded, in PowermeterFilter.Values order
                double[] raw =
                {
                    m.VoltageLL, m.Current, m.ActiveKw, m.ReactiveKvar, m.ApparentKva, m.PowerFactor, m.Frequency,
                    m.VoltageLL + 0.8, m.VoltageLL - 0.5, m.VoltageLL - 0.3,
                    m.Current, m.Current, m.Current,
                };
                double[] filtered = _meterFilters[s].Run(_t, raw, true, sample);

                var values = new Dictionary<string, double>
                {
                    { "General.ActiveEnergy", _activeWh[s] }, { "General.ReactiveEnergy", _reactiveWh[s] }, { "General.ApparentEnergy", _apparentWh[s] },
                };
                for (int i = 0; i < PowermeterFilter.Values.Length; i++) values[PowermeterFilter.Values[i]] = filtered[i];

                _t.W(meter + "CommOk", true);
                foreach (var group in Station.MeterGroups)
                    foreach (var v in Station.MeterValues)
                        _t.W(meter + group + "." + v, values.TryGetValue(group + "." + v, out var value) ? value : 0.0);
            }
        }

        // FB_Communication_Atv600Decoder's result: the ATV660 of the 630 kW VFD, read over Modbus TCP. Output frequency and
        // voltage follow the speed (U/f), the motor gets the meter's power less the drive's losses; nominal 630 kW at 381 rpm.
        private readonly PowermeterFilter _driveFilter = new PowermeterFilter(Station.DriveFilterHp, Station.DriveFilterPh);

        private void WriteDrive()
        {
            const int s = Station.DriveSource;
            const double nominalKw = 630.0, nominalRpm = 381.0;
            var m = _field.Meter(s);
            double speed = _field.DriveSpeed(s);
            double electricalKw = m.ActiveKw * 0.975, mechanicalKw = electricalKw * 0.955;
            double rpm = nominalRpm * speed, torqueNm = rpm > 1.0 ? mechanicalKw * 9549.0 / rpm : 0.0;
            double nominalNm = nominalKw * 9549.0 / nominalRpm;
            // Decoded, in PowermeterFilter.DriveValues order; then FB_Communication_DriveFilter (both requests answer here)
            double[] raw =
            {
                50.0 * speed, m.Current * 0.98, torqueNm / nominalNm * 100.0, m.VoltageLL, 400.0 * speed, mechanicalKw / nominalKw * 100.0,
                torqueNm, mechanicalKw, electricalKw,
            };
            double[] filtered = _driveFilter.Run(_t, raw, true, _meterSample);

            string d = Station.Drive630;
            _t.W(d + "CommOk", true);
            _t.W(d + "ExtraCommOk", true);
            _t.W(d + "StatusWord", _field.DriveRunning(s) ? 0x0637 : 0x0633);
            for (int i = 0; i < PowermeterFilter.DriveValues.Length; i++) _t.W(d + PowermeterFilter.DriveValues[i], filtered[i]);
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
