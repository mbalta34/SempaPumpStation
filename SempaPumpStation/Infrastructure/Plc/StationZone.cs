using System;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>st_Station_ZoneInputs. Arrays are 1-based like the PLC (index 0 unused).</summary>
    public sealed class ZoneInputs
    {
        public readonly bool[] DeviceOpen = new bool[9];
        public readonly bool[] DeviceClosed = new bool[9];
        public readonly bool[] SpringCharged = new bool[9];   // only 7 and 8 are used
        public bool PendantStart, PendantStop, PendantTakeSample, EStopOk, PanelEStopOk;
        public bool DriveRunning, DriveFault;
        /// <summary>A power device of some zone was manipulated: every motor stays stopped until it is reset.</summary>
        public bool SafetyStop;
        public readonly double[] Flowmeters = new double[6];
        public readonly double[] DischargeTransmitters = new double[Station.MaxTransmitterCount + 1];
        public readonly int[] FlowmeterDevices = new int[6];
        public readonly int[] DischargeTransmitterDevices = new int[Station.MaxTransmitterCount + 1];
        public readonly double[] Vacuummeters = new double[Station.MaxVacuummeterCount + 1];
        public readonly int[] VacuummeterDevices = new int[Station.MaxVacuummeterCount + 1];
        public double WaterTemperature, AmbientTemperature, Vibration, Temperature, Sound, Speed, Voltage, Current, Torque, PowerFactor;
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
        /// <summary>Button lamps: start = running, stop = stopped, take sample = 1 s per press, E-stops = off while pressed.</summary>
        public bool LampStart, LampStop, LampTakeSample, LampEStop, LampPanelEStop;
        /// <summary>A power device of this zone was manipulated (latched until reset).</summary>
        public bool Manipulated;

        /// <summary>A copy, for what the outputs actually drive after forcing ("Io".DigitalOutputs).</summary>
        public ZoneOutputs Clone()
        {
            var c = new ZoneOutputs
            {
                RunRequest = RunRequest, SpeedReference = SpeedReference,
                SelectedSource = SelectedSource, SelectedFlowmeter = SelectedFlowmeter,
                LampGreen = LampGreen, LampYellow = LampYellow, LampRed = LampRed, Manipulated = Manipulated,
                LampStart = LampStart, LampStop = LampStop, LampTakeSample = LampTakeSample, LampEStop = LampEStop, LampPanelEStop = LampPanelEStop,
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
    /// break-before-make power devices, E-stop latch, manipulation stop, pump run request, test and sampling, tower lamp.
    /// hp/ph/samples are the zone's StationHp / StationPh / StationSamples tags.
    /// </summary>
    public sealed class StationZone
    {
        private const int SourceCount = 8, SampleCapacity = 50;

        private readonly int _zone;
        private readonly string _hp, _ph, _samples;

        // FB_Station_ZoneHmi
        private readonly HmiParameterInt _powerSource = new HmiParameterInt();
        private readonly HmiParameterInt _flowmeter = new HmiParameterInt();
        private readonly HmiParameterInt _dischargeTransmitter = new HmiParameterInt();
        private readonly HmiParameterInt _vacuummeter = new HmiParameterInt();
        private readonly HmiParameterReal _speedReference = new HmiParameterReal();
        private readonly HmiCommandButton _stop = new HmiCommandButton(), _resetFault = new HmiCommandButton(),
            _startTest = new HmiCommandButton(), _endTest = new HmiCommandButton(),
            _deleteLastSample = new HmiCommandButton(), _clearSamples = new HmiCommandButton(),
            _rotationTest = new HmiCommandButton(), _confirmRotation = new HmiCommandButton();
        private readonly HmiParameterReal _rotationTestTime = new HmiParameterReal();

        private readonly Contactor[] _contactors = new Contactor[7];
        private readonly MagneticSwitch[] _switches = new MagneticSwitch[9];
        private readonly RTrig _startTrigger = new RTrig(), _sampleTrigger = new RTrig();
        private readonly Tp _sampleLamp = new Tp();
        private const double SampleLampMs = 1000;

        private int _selectedSource, _selectedFlowmeter, _selectedTransmitter, _selectedVacuummeter;
        private bool _runRequest, _eStop, _selectionRejected;

        // Rotation test (Station.Rotation* states)
        private bool _rotationRequest, _rotationAllowed;
        private int _rotationState, _rotationSource;

        // The first device (1-8) found manipulated, 0 none; kept until reset
        private int _manipulation;
        private double _rotationElapsedMs;
        private const double RotationDefaultTime = 3.0, RotationMaxTime = 600.0;

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
            _flowmeter.RetainValue = zone == 2 ? 2 : 1;
            _dischargeTransmitter.RetainValue = 1;
            _vacuummeter.RetainValue = 1;
            _speedReference.RetainValue = 80.0;
            _rotationTestTime.RetainValue = 3.0;
        }

        public void Run(PlcTags t, ZoneInputs inputs, bool[] sourceHeldByOtherZone, bool[] flowmeterHeldByOtherZone,
                        int flowmeterCount, int transmitterCount, int vacuummeterCount, PlcCycle c)
        {
            var outputs = Outputs;
            bool testActive = t.B(_samples + "TestActive");

            // Source and flowmeter can only be changed while the pump is stopped
            bool selectionEnabled = !_runRequest && !_rotationRequest && !inputs.DriveRunning;
            _powerSource.Run(t, _hp + "PowerSource", _ph + "PowerSource", 0, false, selectionEnabled);
            _flowmeter.Run(t, _hp + "Flowmeter", _ph + "Flowmeter", 0, true, selectionEnabled);
            _dischargeTransmitter.Run(t, _hp + "DischargeTransmitter", _ph + "DischargeTransmitter", 0, true, true);
            _vacuummeter.Run(t, _hp + "Vacuummeter", _ph + "Vacuummeter", 0, true, true);
            _speedReference.Run(t, _hp + "SpeedReference", _ph + "SpeedReference", 0, true, true);
            _stop.Run(t, _hp + "Stop", _ph + "Stop");
            _resetFault.Run(t, _hp + "ResetFault", _ph + "ResetFault");
            _startTest.Run(t, _hp + "StartTest", _ph + "StartTest", !testActive && !_eStop && _rotationState == Station.RotationConfirmed);
            _endTest.Run(t, _hp + "EndTest", _ph + "EndTest", testActive);
            _deleteLastSample.Run(t, _hp + "DeleteLastSample", _ph + "DeleteLastSample");
            _clearSamples.Run(t, _hp + "ClearSamples", _ph + "ClearSamples");
            _rotationTestTime.Run(t, _hp + "RotationTestTime", _ph + "RotationTestTime", 3.0, true, true);
            _rotationTest.Run(t, _hp + "RotationTest", _ph + "RotationTest", _rotationAllowed);
            _confirmRotation.Run(t, _hp + "ConfirmRotation", _ph + "ConfirmRotation", _rotationState == Station.RotationWaitConfirm);

            bool resetFault = _resetFault.QR;

            // ---------------------------------------------------------------- emergency stop
            // Both E-stops (pendant and panel) are normally closed: their input is FALSE while pressed or the wire is broken
            if (!inputs.EStopOk || !inputs.PanelEStopOk) _eStop = true;
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
                if (_dischargeTransmitter.Value >= 0 && _dischargeTransmitter.Value <= transmitterCount)
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

            // The suction vacuummeter, selected like the discharge transmitter
            if (_vacuummeter.Value != _selectedVacuummeter)
            {
                if (_vacuummeter.Value >= 0 && _vacuummeter.Value <= vacuummeterCount)
                {
                    _selectedVacuummeter = _vacuummeter.Value;
                    _selectionRejected = false;
                }
                else
                {
                    _vacuummeter.Value = _selectedVacuummeter;
                    _selectionRejected = true;
                }
            }

            // ---------------------------------------------------------------- power devices
            // Break-before-make: the selected device closes only when every other device of this zone is open
            // and no other zone has selected or closed the same source. An E-stop opens every device.
            var isOpen = new bool[9];
            var request = new bool[9];
            // After a manipulation (here or in another zone) nothing closes until it is reset: a closed device stays
            // closed, one that opened (by hand, or for break-before-make) stays open, so nothing returns on its own
            bool frozen = _manipulation != 0 || inputs.SafetyStop;
            for (int i = 1; i <= SourceCount; i++)
                isOpen[i] = inputs.DeviceOpen[i] && !inputs.DeviceClosed[i];

            for (int i = 1; i <= SourceCount; i++)
            {
                bool othersOpen = true;
                for (int j = 1; j <= SourceCount; j++)
                    if (j != i && !isOpen[j]) othersOpen = false;
                request[i] = _selectedSource == i && othersOpen && !sourceHeldByOtherZone[i] && !_eStop
                             && (!frozen || (inputs.DeviceClosed[i] && !inputs.DeviceOpen[i]));
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

            // ---------------------------------------------------------------- manipulation
            // A contactor or magnetic switch that changes state without a command stops every motor of the station
            // (FB_Station_Main drops all drive runs). The first such device is kept until it is back in its commanded
            // position and the fault is reset. inputs.SafetyStop is a manipulation in another zone.
            bool anyManipulated = false;
            for (int i = 1; i <= SourceCount; i++)
                if (devices[i].Manipulated)
                {
                    anyManipulated = true;
                    if (_manipulation == 0) _manipulation = i;
                }
            if (resetFault && !anyManipulated) _manipulation = 0;
            bool stopAll = _manipulation != 0 || inputs.SafetyStop;

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

            if (_startTrigger.Q && deviceClosed && !deviceFault && !inputs.DriveFault && !_eStop && !stopAll && !_rotationRequest)
                _runRequest = true;

            // Stop wins over start
            if (inputs.PendantStop || _stop.QR || deviceFault || !deviceClosed || inputs.DriveFault || _eStop || stopAll)
                _runRequest = false;

            // ---------------------------------------------------------------- rotation test
            // Yön testi runs the pump for RotationTestTime seconds and stops it; the operator then confirms the
            // direction, which enables Start test. A new source, a disconnected source or the end of the test clears it.
            if (_rotationTestTime.Value <= 0.0) _rotationTestTime.Value = RotationDefaultTime;
            else if (_rotationTestTime.Value > RotationMaxTime) _rotationTestTime.Value = RotationMaxTime;

            if (_selectedSource != _rotationSource || !deviceClosed)
            {
                _rotationSource = _selectedSource;
                _rotationRequest = false;
                _rotationState = Station.RotationNone;
            }

            _rotationAllowed = deviceClosed && !deviceFault && !inputs.DriveFault && !_eStop && !stopAll
                               && !_runRequest && !inputs.DriveRunning && !testActive
                               && _rotationState != Station.RotationRunning;

            if (_rotationTest.QR && _rotationAllowed)
            {
                _rotationRequest = true;
                _rotationState = Station.RotationRunning;
            }

            // Stop, a fault, the E-stop or a manipulation aborts it unconfirmed
            if (_rotationRequest && (inputs.PendantStop || _stop.QR || deviceFault || inputs.DriveFault || _eStop || stopAll))
            {
                _rotationRequest = false;
                _rotationState = Station.RotationNone;
            }

            // TON: elapsed while requested
            _rotationElapsedMs = _rotationRequest ? _rotationElapsedMs + c.DtMs : 0.0;
            if (_rotationRequest && _rotationElapsedMs >= _rotationTestTime.Value * 1000.0)
            {
                _rotationRequest = false;
                _rotationState = Station.RotationWaitConfirm;
            }

            if (_confirmRotation.QR && _rotationState == Station.RotationWaitConfirm)
                _rotationState = Station.RotationConfirmed;

            outputs.RunRequest = _runRequest || _rotationRequest;
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
            if (_selectedTransmitter > 0 && _selectedTransmitter <= transmitterCount)
            {
                dischargePressure = inputs.DischargeTransmitters[_selectedTransmitter];
                dischargeTransmitterDevice = inputs.DischargeTransmitterDevices[_selectedTransmitter];
            }
            double suctionPressure = 0;
            int vacuummeterDevice = 0;
            if (_selectedVacuummeter > 0 && _selectedVacuummeter <= vacuummeterCount)
            {
                suctionPressure = inputs.Vacuummeters[_selectedVacuummeter];
                vacuummeterDevice = inputs.VacuummeterDevices[_selectedVacuummeter];
            }

            // ---------------------------------------------------------------- test
            // Start test opens a new test: it clears the samples of the previous test and stamps the start time.
            // End test closes it. The samples stay in the buffer until the next start, so the HMI can archive them.
            int count = t.I(_samples + "Count");
            if (_startTest.QR && !testActive && !_eStop && _rotationState == Station.RotationConfirmed)
            {
                testActive = true;
                count = 0;
                // The test number is retained in the PLC ("StationCounters".NextTestNo), shared by the zones
                int next = Math.Max(1, t.I(Station.NextTestNo));
                t.W(_samples + "TestNo", next);
                t.W(Station.NextTestNo, next + 1);
                WriteDtl(t, _samples + "TestStarted", DateTime.Now);
                WriteDtl(t, _samples + "TestEnded", new DateTime(1970, 1, 1));
            }
            if (_endTest.QR && testActive)
            {
                testActive = false;
                _rotationState = Station.RotationNone;   // the next test is a new pump: check its direction again
                WriteDtl(t, _samples + "TestEnded", DateTime.Now);
            }

            // ---------------------------------------------------------------- samples
            // Samples are taken, deleted and cleared only while a test is active.
            // The HMI may write Count, so keep it inside the buffer.
            count = Math.Max(0, Math.Min(SampleCapacity, count));

            _sampleTrigger.Run(inputs.PendantTakeSample);
            _sampleLamp.Run(_sampleTrigger.Q, SampleLampMs, c);

            if (_sampleTrigger.Q && testActive && count < SampleCapacity)
            {
                count++;
                string s = Station.Sample(_zone, count);
                WriteDtl(t, s + "Timestamp", DateTime.Now);
                t.W(s + "PowerSource", _selectedSource);
                t.W(s + "Flowmeter", _selectedFlowmeter);
                t.W(s + "DischargeTransmitter", _selectedTransmitter);
                t.W(s + "Vacuummeter", _selectedVacuummeter);
                t.W(s + "FlowmeterDevice", flowmeterDevice);
                t.W(s + "DischargeTransmitterDevice", dischargeTransmitterDevice);
                t.W(s + "VacuummeterDevice", vacuummeterDevice);
                t.W(s + "Flow", flow);
                t.W(s + "DischargePressure", dischargePressure);
                t.W(s + "SuctionPressure", suctionPressure);
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
                t.W(s + "Torque", inputs.Torque);
                t.W(s + "PowerFactor", inputs.PowerFactor);
            }

            if (_deleteLastSample.QR && testActive && count > 0) count--;
            if (_clearSamples.QR && testActive) count = 0;

            t.W(_samples + "TestActive", testActive);
            t.W(_samples + "Count", count);

            // ---------------------------------------------------------------- tower lamp
            outputs.LampRed = _eStop || deviceFault || inputs.DriveFault || stopAll;
            outputs.Manipulated = _manipulation != 0;

            // ---------------------------------------------------------------- button lamps
            outputs.LampStart = inputs.DriveRunning;
            outputs.LampStop = !inputs.DriveRunning;
            outputs.LampTakeSample = _sampleLamp.Q;
            outputs.LampEStop = inputs.EStopOk;
            outputs.LampPanelEStop = inputs.PanelEStopOk;
            outputs.LampGreen = !outputs.LampRed && (inputs.DriveRunning || ((_runRequest || _rotationRequest) && c.Flash1s));
            outputs.LampYellow = !outputs.LampRed && !_runRequest && !_rotationRequest && !inputs.DriveRunning
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
            t.W(_ph + "SuctionPressure", suctionPressure);
            t.W(_ph + "FlowmeterDevice", flowmeterDevice);
            t.W(_ph + "DischargeTransmitterDevice", dischargeTransmitterDevice);
            t.W(_ph + "VacuummeterDevice", vacuummeterDevice);
            t.W(_ph + "LampGreen", outputs.LampGreen);
            t.W(_ph + "LampYellow", outputs.LampYellow);
            t.W(_ph + "LampRed", outputs.LampRed);
            t.W(_ph + "RotationState", _rotationState);
            t.W(_ph + "SafetyStop", stopAll);
            t.W(_ph + "ManipulatedDevice", _manipulation);
            t.W(_ph + "RotationRemaining", _rotationRequest ? Math.Max(0.0, _rotationTestTime.Value - _rotationElapsedMs / 1000.0) : 0.0);
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
