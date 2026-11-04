using System;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>st_Station_ZoneInputs. Arrays are 1-based like the PLC (index 0 unused).</summary>
    public sealed class ZoneInputs
    {
        public readonly bool[] DeviceOpen = new bool[9];
        public readonly bool[] DeviceClosed = new bool[9];
        public readonly bool[] SpringCharged = new bool[9];   // only 7 and 8 are used
        public bool PendantStart, PendantStop, PendantTakeSample, EStopOk;
        public bool DriveRunning, DriveFault;
        public readonly double[] Flowmeters = new double[6];
        public readonly double[] DischargeTransmitters = new double[5];
        public readonly int[] FlowmeterDevices = new int[6];
        public readonly int[] DischargeTransmitterDevices = new int[5];
        public int VacuummeterDevice;
        public double Vacuummeter, WaterTemperature, AmbientTemperature, Vibration, Temperature, Sound, Speed, Voltage, Current;
    }

    /// <summary>st_Station_ZoneOutputs.</summary>
    public sealed class ZoneOutputs
    {
        public readonly bool[] ContactorClose = new bool[7];
        public readonly bool[] SwitchOpen = new bool[9];
        public readonly bool[] SwitchClose = new bool[9];
        public readonly bool[] SwitchChargeSpring = new bool[9];
        public bool RunRequest;
        public double SpeedReference;
        public int SelectedSource, SelectedFlowmeter;
        public bool LampGreen, LampYellow, LampRed;

        /// <summary>A copy, for what the outputs actually drive after forcing ("Io".DigitalOutputs).</summary>
        public ZoneOutputs Clone()
        {
            var c = new ZoneOutputs
            {
                RunRequest = RunRequest, SpeedReference = SpeedReference,
                SelectedSource = SelectedSource, SelectedFlowmeter = SelectedFlowmeter,
                LampGreen = LampGreen, LampYellow = LampYellow, LampRed = LampRed,
            };
            ContactorClose.CopyTo(c.ContactorClose, 0);
            SwitchOpen.CopyTo(c.SwitchOpen, 0);
            SwitchClose.CopyTo(c.SwitchClose, 0);
            SwitchChargeSpring.CopyTo(c.SwitchChargeSpring, 0);
            return c;
        }
    }

    /// <summary>
    /// Port of FB_Station_Zone (with its FB_Station_ZoneHmi) as it is in MainPLC: selection guard,
    /// break-before-make power devices, E-stop latch, pump run request, test and sampling, tower lamp.
    /// hp/ph/samples are the zone's StationHp / StationPh / StationSamples tags.
    /// </summary>
    public sealed class StationZone
    {
        private const int SourceCount = 8, TransmitterCount = 4, SampleCapacity = 50;

        private readonly int _zone;
        private readonly string _hp, _ph, _samples;

        // FB_Station_ZoneHmi
        private readonly HmiParameterInt _powerSource = new HmiParameterInt();
        private readonly HmiParameterInt _flowmeter = new HmiParameterInt();
        private readonly HmiParameterInt _dischargeTransmitter = new HmiParameterInt();
        private readonly HmiParameterReal _speedReference = new HmiParameterReal();
        private readonly HmiCommandButton _stop = new HmiCommandButton(), _resetFault = new HmiCommandButton(),
            _startTest = new HmiCommandButton(), _endTest = new HmiCommandButton(),
            _deleteLastSample = new HmiCommandButton(), _clearSamples = new HmiCommandButton();

        private readonly Contactor[] _contactors = new Contactor[7];
        private readonly MagneticSwitch[] _switches = new MagneticSwitch[9];
        private readonly RTrig _startTrigger = new RTrig(), _sampleTrigger = new RTrig();

        private int _selectedSource, _selectedFlowmeter, _selectedTransmitter;
        private bool _runRequest, _eStop, _selectionRejected;

        public ZoneOutputs Outputs { get; } = new ZoneOutputs();

        public StationZone(int zone)
        {
            _zone = zone;
            _hp = Station.ZoneHp(zone);
            _ph = Station.ZonePh(zone);
            _samples = Station.Samples(zone);
            for (int i = 1; i <= 6; i++) _contactors[i] = new Contactor();
            _switches[7] = new MagneticSwitch();
            _switches[8] = new MagneticSwitch();

            // Retained selections, as the PLC would have them after a restart
            _flowmeter.RetainValue = zone == 2 ? 2 : 1;   // zones 1 and 2 share the flowmeters
            _dischargeTransmitter.RetainValue = 2;
            _speedReference.RetainValue = 80.0;
        }

        public void Run(PlcTags t, ZoneInputs inputs, bool[] sourceHeldByOtherZone, bool[] flowmeterHeldByOtherZone,
                        int flowmeterCount, PlcCycle c)
        {
            var outputs = Outputs;
            bool testActive = t.B(_samples + "TestActive");

            // Source and flowmeter can only be changed while the pump is stopped
            bool selectionEnabled = !_runRequest && !inputs.DriveRunning;
            _powerSource.Run(t, _hp + "PowerSource", _ph + "PowerSource", 0, false, selectionEnabled);
            _flowmeter.Run(t, _hp + "Flowmeter", _ph + "Flowmeter", 0, true, selectionEnabled);
            _dischargeTransmitter.Run(t, _hp + "DischargeTransmitter", _ph + "DischargeTransmitter", 0, true, true);
            _speedReference.Run(t, _hp + "SpeedReference", _ph + "SpeedReference", 0, true, true);
            _stop.Run(t, _hp + "Stop", _ph + "Stop");
            _resetFault.Run(t, _hp + "ResetFault", _ph + "ResetFault");
            _startTest.Run(t, _hp + "StartTest", _ph + "StartTest", !testActive && !_eStop);
            _endTest.Run(t, _hp + "EndTest", _ph + "EndTest", testActive);
            _deleteLastSample.Run(t, _hp + "DeleteLastSample", _ph + "DeleteLastSample");
            _clearSamples.Run(t, _hp + "ClearSamples", _ph + "ClearSamples");

            bool resetFault = _resetFault.QR;

            // ---------------------------------------------------------------- emergency stop
            // The E-stop contact is normally closed, so eStopOk is FALSE while the button is pressed or the wire is broken.
            if (!inputs.EStopOk) _eStop = true;
            else if (resetFault) _eStop = false;

            // ---------------------------------------------------------------- selection guard
            // A rejected value is written back into the parameter, which pushes it to the HMI again
            if (_powerSource.Value != _selectedSource)
            {
                _selectionRejected = true;
                if (_powerSource.Value == 0) _selectionRejected = false;
                else if (_powerSource.Value > 0 && _powerSource.Value <= SourceCount)
                    _selectionRejected = sourceHeldByOtherZone[_powerSource.Value];

                if (_selectionRejected) _powerSource.Value = _selectedSource;
                else _selectedSource = _powerSource.Value;
            }

            if (_flowmeter.Value != _selectedFlowmeter)
            {
                _selectionRejected = true;
                if (_flowmeter.Value == 0) _selectionRejected = false;
                else if (_flowmeter.Value > 0 && _flowmeter.Value <= flowmeterCount)
                    _selectionRejected = flowmeterHeldByOtherZone[_flowmeter.Value];

                if (_selectionRejected) _flowmeter.Value = _selectedFlowmeter;
                else _selectedFlowmeter = _flowmeter.Value;
            }

            if (_dischargeTransmitter.Value != _selectedTransmitter)
            {
                if (_dischargeTransmitter.Value >= 0 && _dischargeTransmitter.Value <= TransmitterCount)
                {
                    _selectedTransmitter = _dischargeTransmitter.Value;
                    _selectionRejected = false;
                }
                else
                {
                    _dischargeTransmitter.Value = _selectedTransmitter;
                    _selectionRejected = true;
                }
            }

            // ---------------------------------------------------------------- power devices
            // Break-before-make: the selected device closes only when every other device of this zone is open
            // and no other zone has selected or closed the same source. An E-stop opens every device.
            var isOpen = new bool[9];
            var request = new bool[9];
            for (int i = 1; i <= SourceCount; i++)
                isOpen[i] = inputs.DeviceOpen[i] && !inputs.DeviceClosed[i];

            for (int i = 1; i <= SourceCount; i++)
            {
                bool othersOpen = true;
                for (int j = 1; j <= SourceCount; j++)
                    if (j != i && !isOpen[j]) othersOpen = false;
                request[i] = _selectedSource == i && othersOpen && !sourceHeldByOtherZone[i] && !_eStop;
            }

            var devices = new PowerDevice[9];
            for (int i = 1; i <= 6; i++)
            {
                _contactors[i].Run(request[i], inputs.DeviceOpen[i], inputs.DeviceClosed[i], resetFault, c);
                outputs.ContactorClose[i] = _contactors[i].CmdClose;
                devices[i] = _contactors[i];
            }
            for (int i = 7; i <= 8; i++)
            {
                _switches[i].Run(request[i], inputs.DeviceOpen[i], inputs.DeviceClosed[i], inputs.SpringCharged[i], resetFault, c);
                outputs.SwitchClose[i] = _switches[i].CmdClose;
                outputs.SwitchOpen[i] = _switches[i].CmdOpen;
                outputs.SwitchChargeSpring[i] = _switches[i].CmdChargeSpring;
                devices[i] = _switches[i];
            }

            bool deviceFault = false, allOpen = true;
            for (int i = 1; i <= SourceCount; i++)
            {
                deviceFault |= devices[i].Fault;
                allOpen &= isOpen[i];
            }

            bool deviceClosed = false, closing = false;
            if (_selectedSource > 0)
            {
                deviceClosed = devices[_selectedSource].IsClosed;
                closing = request[_selectedSource];
            }

            int powerState;
            if (deviceFault) powerState = Station.StateFault;
            else if (deviceClosed) powerState = Station.StateConnected;
            else if (closing) powerState = Station.StateClosing;
            else if (allOpen) powerState = Station.StateIdle;
            else powerState = Station.StateOpening;
            t.W(_ph + "PowerState", powerState);

            // ---------------------------------------------------------------- pump
            _startTrigger.Run(inputs.PendantStart);

            if (_startTrigger.Q && deviceClosed && !deviceFault && !inputs.DriveFault && !_eStop)
                _runRequest = true;

            // Stop wins over start
            if (inputs.PendantStop || _stop.QR || deviceFault || !deviceClosed || inputs.DriveFault || _eStop)
                _runRequest = false;

            outputs.RunRequest = _runRequest;
            outputs.SpeedReference = _speedReference.Value;
            outputs.SelectedSource = _selectedSource;
            outputs.SelectedFlowmeter = _selectedFlowmeter;

            // ---------------------------------------------------------------- measurements
            double flow = 0, dischargePressure = 0;
            int flowmeterDevice = 0, dischargeTransmitterDevice = 0;
            if (_selectedFlowmeter > 0 && _selectedFlowmeter <= flowmeterCount)
            {
                flow = inputs.Flowmeters[_selectedFlowmeter];
                flowmeterDevice = inputs.FlowmeterDevices[_selectedFlowmeter];
            }
            if (_selectedTransmitter > 0)
            {
                dischargePressure = inputs.DischargeTransmitters[_selectedTransmitter];
                dischargeTransmitterDevice = inputs.DischargeTransmitterDevices[_selectedTransmitter];
            }

            // ---------------------------------------------------------------- test
            // Start test opens a new test: it clears the samples of the previous test and stamps the start time.
            // End test closes it. The samples stay in the buffer until the next start, so the HMI can archive them.
            int count = t.I(_samples + "Count");
            if (_startTest.QR && !testActive && !_eStop)
            {
                testActive = true;
                count = 0;
                WriteDtl(t, _samples + "TestStarted", DateTime.Now);
                WriteDtl(t, _samples + "TestEnded", new DateTime(1970, 1, 1));
            }
            if (_endTest.QR && testActive)
            {
                testActive = false;
                WriteDtl(t, _samples + "TestEnded", DateTime.Now);
            }

            // ---------------------------------------------------------------- samples
            // Samples are taken, deleted and cleared only while a test is active.
            // The HMI may write Count, so keep it inside the buffer.
            count = Math.Max(0, Math.Min(SampleCapacity, count));

            _sampleTrigger.Run(inputs.PendantTakeSample);

            if (_sampleTrigger.Q && testActive && count < SampleCapacity)
            {
                count++;
                string s = Station.Sample(_zone, count);
                WriteDtl(t, s + "Timestamp", DateTime.Now);
                t.W(s + "PowerSource", _selectedSource);
                t.W(s + "Flowmeter", _selectedFlowmeter);
                t.W(s + "DischargeTransmitter", _selectedTransmitter);
                t.W(s + "FlowmeterDevice", flowmeterDevice);
                t.W(s + "DischargeTransmitterDevice", dischargeTransmitterDevice);
                t.W(s + "VacuummeterDevice", inputs.VacuummeterDevice);
                t.W(s + "Flow", flow);
                t.W(s + "DischargePressure", dischargePressure);
                t.W(s + "SuctionPressure", inputs.Vacuummeter);
                t.W(s + "WaterTemperature", inputs.WaterTemperature);
                t.W(s + "AmbientTemperature", inputs.AmbientTemperature);
                t.W(s + "Vibration", inputs.Vibration);
                t.W(s + "Temperature", inputs.Temperature);
                t.W(s + "Sound", inputs.Sound);
                t.W(s + "Speed", inputs.Speed);
                t.W(s + "Voltage", inputs.Voltage);
                t.W(s + "Current", inputs.Current);
                t.W(s + "SpeedReference", _speedReference.Value);
                t.W(s + "PumpRunning", inputs.DriveRunning);
            }

            if (_deleteLastSample.QR && testActive && count > 0) count--;
            if (_clearSamples.QR && testActive) count = 0;

            t.W(_samples + "TestActive", testActive);
            t.W(_samples + "Count", count);

            // ---------------------------------------------------------------- tower lamp
            outputs.LampRed = _eStop || deviceFault || inputs.DriveFault;
            outputs.LampGreen = !outputs.LampRed && (inputs.DriveRunning || (_runRequest && c.Flash1s));
            outputs.LampYellow = !outputs.LampRed && !_runRequest && !inputs.DriveRunning
                                 && (powerState == Station.StateConnected
                                     || (c.Flash1s && (powerState == Station.StateClosing || powerState == Station.StateOpening)));

            // ---------------------------------------------------------------- status
            t.W(_ph + "PumpRunning", inputs.DriveRunning);
            t.W(_ph + "DeviceFault", deviceFault);
            t.W(_ph + "DriveFault", inputs.DriveFault);
            t.W(_ph + "EStop", _eStop);
            t.W(_ph + "SelectionRejected", _selectionRejected);
            t.W(_ph + "TestActive", testActive);
            t.W(_ph + "SampleCount", count);
            t.W(_ph + "SampleBufferFull", count >= SampleCapacity);
            t.W(_ph + "Flow", flow);
            t.W(_ph + "DischargePressure", dischargePressure);
            t.W(_ph + "SuctionPressure", inputs.Vacuummeter);
            t.W(_ph + "FlowmeterDevice", flowmeterDevice);
            t.W(_ph + "DischargeTransmitterDevice", dischargeTransmitterDevice);
            t.W(_ph + "VacuummeterDevice", inputs.VacuummeterDevice);
            t.W(_ph + "LampGreen", outputs.LampGreen);
            t.W(_ph + "LampYellow", outputs.LampYellow);
            t.W(_ph + "LampRed", outputs.LampRed);
        }

        // RD_LOC_T into a DTL
        private static void WriteDtl(PlcTags t, string dtl, DateTime v)
        {
            t.W(dtl + ".Year", v.Year);
            t.W(dtl + ".Month", v.Month);
            t.W(dtl + ".Day", v.Day);
            t.W(dtl + ".Weekday", (int)v.DayOfWeek + 1);
            t.W(dtl + ".Hour", v.Hour);
            t.W(dtl + ".Minute", v.Minute);
            t.W(dtl + ".Second", v.Second);
            t.W(dtl + ".Nanosecond", v.Millisecond * 1000000);
        }
    }
}
