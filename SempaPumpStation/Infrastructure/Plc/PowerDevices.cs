namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>Common outputs of the two power-device FBs, so a zone can treat them alike.</summary>
    public abstract class PowerDevice
    {
        public bool IsOpen { get; protected set; }
        public bool IsClosed { get; protected set; }
        public bool Fault { get; protected set; }
    }

    /// <summary>Port of FB_Station_Contactor.</summary>
    public sealed class Contactor : PowerDevice
    {
        private const double FeedbackTimeoutMs = 3000;
        private readonly Ton _feedbackTimer = new Ton();
        private bool _faulted, _previousCommand;

        public bool CmdClose { get; private set; }

        public void Run(bool request, bool fbOpen, bool fbClosed, bool resetFault, PlcCycle c)
        {
            // Both or neither feedback on counts as neither open nor closed
            IsOpen = fbOpen && !fbClosed;
            IsClosed = fbClosed && !fbOpen;

            if (resetFault) _faulted = false;

            CmdClose = request && !_faulted;

            // The timer restarts whenever the command changes, so each switching gets the full timeout
            bool discrepancy = (CmdClose && !IsClosed) || (!CmdClose && !IsOpen);
            _feedbackTimer.Run(discrepancy && CmdClose == _previousCommand, FeedbackTimeoutMs, c);
            _previousCommand = CmdClose;

            if (_feedbackTimer.Q)
            {
                _faulted = true;
                CmdClose = false;
            }
            Fault = _faulted;
        }
    }

    /// <summary>Port of FB_Station_MagneticSwitch.</summary>
    public sealed class MagneticSwitch : PowerDevice
    {
        private const double PulseMs = 500, FeedbackTimeoutMs = 3000, SpringTimeoutMs = 15000;
        private readonly Tp _closePulse = new Tp(), _openPulse = new Tp();
        private readonly Ton _feedbackTimer = new Ton(), _springTimer = new Ton();
        private bool _faulted, _closeIssued, _previousCommand;

        public bool CmdClose { get; private set; }
        public bool CmdOpen { get; private set; }
        public bool CmdChargeSpring { get; private set; }

        public void Run(bool request, bool fbOpen, bool fbClosed, bool springCharged, bool resetFault, PlcCycle c)
        {
            IsOpen = fbOpen && !fbClosed;
            IsClosed = fbClosed && !fbOpen;

            if (resetFault) _faulted = false;

            bool command = request && !_faulted;

            // Close only with a charged spring; one pulse per request, a fault reset re-arms both pulses
            _closePulse.Run(command && IsOpen && springCharged && !resetFault, PulseMs, c);
            CmdClose = _closePulse.Q && command;
            if (CmdClose) _closeIssued = true;
            else if (!command) _closeIssued = false;

            // Also opens after a fault, because a fault drops the command
            _openPulse.Run(!command && !IsOpen && !resetFault, PulseMs, c);
            CmdOpen = _openPulse.Q && !command;

            CmdChargeSpring = IsOpen && !springCharged && !_faulted;
            _springTimer.Run(CmdChargeSpring, SpringTimeoutMs, c);

            bool discrepancy = (command && _closeIssued && !IsClosed) || (!command && !IsOpen);
            _feedbackTimer.Run(discrepancy && command == _previousCommand, FeedbackTimeoutMs, c);
            _previousCommand = command;

            if (_feedbackTimer.Q || _springTimer.Q)
            {
                _faulted = true;
                CmdClose = false;
                CmdChargeSpring = false;
            }
            Fault = _faulted;
        }
    }
}
