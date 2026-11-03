using System;
using System.Collections.Generic;

namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// The simulated field around the PLC: contactors and magnetic switches with their feedbacks,
    /// the eight drives, and one test pump per zone. Commands come from the PLC outputs; the
    /// operator-side field signals (pendants, E-stops, discharge valves, injected faults) come from
    /// the "Sim.*" tags. Analog sensors are produced as raw S7 counts from each mounted device's own
    /// range, so a channel scaled with the wrong parameters reads wrong, as it would on site.
    /// </summary>
    public sealed class FieldSimulation
    {
        private const double ContactorDelayMs = 150, SpringChargeMs = 3000, DriveStartDelayMs = 800;
        private const double RawMax = 32511, RawWireBreak = 32767;

        private readonly PlcTags _t;
        private readonly Random _rnd = new Random(7);

        // Physical device state per zone (1-based zone and source)
        private readonly bool[,] _closed = new bool[4, 9];
        private readonly double[,] _coilTimer = new double[4, 9];
        private readonly bool[,] _springCharged = new bool[4, 9];
        private readonly double[,] _springTimer = new double[4, 9];

        // Drives (1-based source)
        private readonly bool[] _driveRunning = new bool[9];
        private readonly double[] _driveSpeed = new double[9];   // fraction of rated speed
        private readonly double[] _driveStartTimer = new double[9];

        // Hydraulics per zone
        private readonly double[] _flow = new double[4], _head = new double[4], _suction = new double[4];
        private readonly double[] _waterTemp = { 0, 18.0, 18.5, 17.8 }, _motorTemp = { 0, 24.0, 24.0, 24.0 };

        private readonly Dictionary<string, int> _raw = new Dictionary<string, int>();

        public FieldSimulation(PlcTags tags)
        {
            _t = tags;
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                for (int s = 1; s <= Station.SourceCount; s++) _closed[z, s] = false;
                _t.W(Station.SimZone(z) + "ValveOpening", 60.0);
            }
        }

        // ---------------------------------------------------------------- PLC inputs
        public bool EStopPressed(int z) => _t.B(Station.SimZone(z) + "EStopPressed");
        private bool FeedbackFault(int z) => _t.B(Station.SimZone(z) + "FeedbackFault");

        // A feedback fault removes the feedback supply: neither open nor closed is reported
        public bool DeviceOpen(int z, int s) => !FeedbackFault(z) && !_closed[z, s];
        public bool DeviceClosed(int z, int s) => !FeedbackFault(z) && _closed[z, s];
        public bool SpringCharged(int z, int s) => _springCharged[z, s];
        public bool PendantStart(int z) => _t.B(Station.SimZone(z) + "PendantStart");
        public bool PendantStop(int z) => _t.B(Station.SimZone(z) + "PendantStop");
        public bool PendantTakeSample(int z) => _t.B(Station.SimZone(z) + "PendantTakeSample");
        public bool EStopOk(int z) => !EStopPressed(z);   // normally closed contact
        public bool DriveRunning(int s) => _driveRunning[s];
        public bool DriveFault(int s) => _t.B(Station.SimDrive(s) + "Fault");

        public int Raw(string channel)
        {
            int r;
            return _raw.TryGetValue(channel, out r) ? r : 0;
        }

        // ---------------------------------------------------------------- field update
        /// <summary>Advance the field by one cycle with the PLC's latest outputs.</summary>
        public void Update(ZoneOutputs[] outputs, bool[] driveRun, double[] driveSpeedReference, Func<string, int> mountedDevice, PlcCycle c)
        {
            double dt = c.DtMs / 1000.0;

            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                var o = outputs[z];
                bool eStop = EStopPressed(z);   // the hardwired E-stop circuit drops every coil of the zone

                for (int s = 1; s <= 6; s++)
                    _closed[z, s] = FollowCoil(z, s, o.ContactorClose[s] && !eStop, c);

                for (int s = 7; s <= 8; s++)
                {
                    if (o.SwitchClose[s] && _springCharged[z, s] && !eStop && !_closed[z, s])
                    {
                        _closed[z, s] = true;
                        _springCharged[z, s] = false;   // closing uses the spring energy
                    }
                    if (o.SwitchOpen[s] || eStop) _closed[z, s] = false;   // shunt trip / undervoltage release

                    if (o.SwitchChargeSpring[s] && !_springCharged[z, s])
                    {
                        _springTimer[z, s] += c.DtMs;
                        if (_springTimer[z, s] >= SpringChargeMs) _springCharged[z, s] = true;
                    }
                    else _springTimer[z, s] = 0;
                }
            }

            // Drives: a drive has power while one zone has its device closed
            for (int s = 1; s <= Station.SourceCount; s++)
            {
                int zone = FedZone(s);
                bool powered = zone > 0;
                bool start = driveRun[s] && powered && !DriveFault(s);

                if (start && !_driveRunning[s])
                {
                    _driveStartTimer[s] += c.DtMs;
                    if (_driveStartTimer[s] >= DriveStartDelayMs) _driveRunning[s] = true;
                }
                else if (!start)
                {
                    _driveStartTimer[s] = 0;
                    _driveRunning[s] = false;
                }

                var info = Station.Source(s);
                double target = !_driveRunning[s] ? 0.0
                              : info.IsVfd ? Clamp(driveSpeedReference[s] / 100.0, 0.0, 1.0)
                              : 1.0;
                double rate = _driveRunning[s] ? (info.IsVfd ? 0.20 : 0.30) : (powered ? 0.25 : 0.12);   // coasting without power
                _driveSpeed[s] = Approach(_driveSpeed[s], target, rate * dt);
                _t.W(Station.SimDrive(s) + "Running", _driveRunning[s]);
            }

            for (int z = 1; z <= Station.ZoneCount; z++) UpdatePump(z, dt);
            UpdateSensors(mountedDevice);
        }

        private bool FollowCoil(int z, int s, bool coil, PlcCycle c)
        {
            if (coil == _closed[z, s]) { _coilTimer[z, s] = 0; return _closed[z, s]; }
            _coilTimer[z, s] += c.DtMs;
            return _coilTimer[z, s] >= ContactorDelayMs ? coil : _closed[z, s];
        }

        /// <summary>The zone that has source s connected, 0 = none.</summary>
        public int FedZone(int s)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
                if (_closed[z, s]) return z;
            return 0;
        }

        private int ConnectedSource(int z)
        {
            for (int s = 1; s <= Station.SourceCount; s++)
                if (_closed[z, s]) return s;
            return 0;
        }

        // ---------------------------------------------------------------- pump model
        // Pump curve H = s²·H0 − a·Q², against a system of static head plus a throttle valve.
        // The pump under test is sized after the source it is connected to.
        private static void PumpSize(int source, out double h0, out double qMax, out double ratedRpm)
        {
            switch (source)
            {
                case 1: case 2: h0 = 35; qMax = 60; ratedRpm = 2900; break;
                case 3: case 4: h0 = 60; qMax = 230; ratedRpm = 2950; break;
                case 5: case 6: h0 = 80; qMax = 520; ratedRpm = 2960; break;
                case 7: h0 = 110; qMax = 1900; ratedRpm = 1485; break;
                case 8: h0 = 115; qMax = 2000; ratedRpm = 1488; break;
                default: h0 = 0; qMax = 1; ratedRpm = 0; break;
            }
        }

        private double _speedRpm1, _speedRpm2, _speedRpm3;

        private void UpdatePump(int z, double dt)
        {
            int source = ConnectedSource(z);
            double s = source > 0 ? _driveSpeed[source] : 0.0;
            PumpSize(source, out double h0, out double qMax, out double ratedRpm);

            const double staticHead = 2.0;
            double a = h0 / (qMax * qMax);
            double valve = Clamp(_t.R(Station.SimZone(z) + "ValveOpening") / 100.0, 0.0, 1.0);
            double resistance = 0.45 * a / Math.Max(0.0004, valve * valve);   // closed valve → shut-off
            double driving = s * s * h0 - staticHead;
            double q = driving > 0 ? Math.Sqrt(driving / (a + resistance)) : 0.0;
            double h = Math.Max(0.0, s * s * h0 - a * q * q);
            double ps = -0.04 - 0.30 * (q / qMax) * (q / qMax);

            // First-order lag so the readings settle like a real line
            double k = Math.Min(1.0, dt / 0.8);
            _flow[z] += (q - _flow[z]) * k;
            _head[z] += (h - _head[z]) * k;
            _suction[z] += (ps - _suction[z]) * k;

            double load = qMax > 1 ? _flow[z] / qMax : 0;
            _waterTemp[z] += (s > 0.1 ? 0.004 * s * (1.0 - load) + 0.0008 : -0.0005) * dt;
            _motorTemp[z] += ((24.0 + 48.0 * s * (0.4 + load)) - _motorTemp[z]) * Math.Min(1.0, dt / 90.0);

            double rpm = s * ratedRpm;
            if (z == 1) _speedRpm1 = rpm; else if (z == 2) _speedRpm2 = rpm; else _speedRpm3 = rpm;
        }

        private double ZoneRpm(int z) => z == 1 ? _speedRpm1 : z == 2 ? _speedRpm2 : _speedRpm3;
        private double ZoneSpeedFraction(int z) { int s = ConnectedSource(z); return s > 0 ? _driveSpeed[s] : 0.0; }

        // ---------------------------------------------------------------- sensors
        private void UpdateSensors(Func<string, int> mountedDevice)
        {
            for (int z = 1; z <= Station.ZoneCount; z++)
            {
                double pd = _suction[z] + _head[z] / 10.197;
                for (int p = 1; p <= Station.TransmitterCount; p++)
                    Instrument(Station.TransmitterPosition(z, p), Station.PressureTransmitter, pd + Noise(0.004), mountedDevice);
                Instrument(Station.VacuummeterPosition(z), Station.Vacuummeter, _suction[z] + Noise(0.002), mountedDevice);

                double sp = ZoneSpeedFraction(z);
                Sensor("Zone" + z + "_WaterTemperature", _waterTemp[z] + Noise(0.03), 0, 100);
                Sensor("Zone" + z + "_AmbientTemperature", 22.0 + 0.4 * Math.Sin(_waterTemp[z]) + Noise(0.05), 0, 100);
                Sensor("Zone" + z + "_Vibration", 0.15 + 2.6 * sp + Noise(0.08), 0, 25);
                Sensor("Zone" + z + "_Temperature", _motorTemp[z] + Noise(0.05), 0, 150);
                Sensor("Zone" + z + "_Sound", 42.0 + 38.0 * sp + Noise(0.4), 30, 130);
                Sensor("Zone" + z + "_Speed", ZoneRpm(z) + (sp > 0.01 ? Noise(2.0) : 0), 0, 3600);
            }

            // The flow of a zone passes through the flowmeter it has selected
            for (int f = 1; f <= Station.SharedFlowmeterCount; f++)
            {
                double q = 0;
                for (int z = 1; z <= 2; z++)
                    if (_t.I(Station.ZonePh(z) + "Flowmeter.Value") == f) q = _flow[z];
                Instrument("Shared_FM" + f, Station.Flowmeter, q + (q > 0.05 ? Noise(q * 0.004) : 0), mountedDevice);
            }
            for (int f = 1; f <= Station.Zone3FlowmeterCount; f++)
            {
                double q = _t.I(Station.ZonePh(3) + "Flowmeter.Value") == f ? _flow[3] : 0;
                Instrument("Zone3_FM" + f, Station.Flowmeter, q + (q > 0.05 ? Noise(q * 0.004) : 0), mountedDevice);
            }
        }

        // A mounted device converts with its own range; an empty position reads as an open circuit
        private void Instrument(string position, int category, double value, Func<string, int> mountedDevice)
        {
            int device = mountedDevice(position);
            if (device <= 0) { _raw[position] = (int)RawWireBreak; return; }

            string e = Station.LibraryEntry(category, device);
            _raw[position] = ToRaw(value, _t.R(e + "MinPv"), _t.R(e + "MaxPv"), _t.I(e + "MinRaw"), _t.I(e + "MaxRaw"));
        }

        private void Sensor(string channel, double value, double minPv, double maxPv) =>
            _raw[channel] = ToRaw(value, minPv, maxPv, 0, 27648);

        private static int ToRaw(double value, double minPv, double maxPv, int minRaw, int maxRaw)
        {
            if (maxPv == minPv) return minRaw;
            double raw = minRaw + (value - minPv) / (maxPv - minPv) * (maxRaw - minRaw);
            return (int)Math.Round(Clamp(raw, -RawMax, RawMax));   // S7 over-/underrange stops at ±32511
        }

        private double Noise(double amplitude) => (_rnd.NextDouble() * 2.0 - 1.0) * amplitude;
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        private static double Approach(double v, double target, double step) =>
            v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);
    }
}
