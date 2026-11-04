namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// Port of FB_Io_DigitalOutput with FB_Io_DigitalOutputHmi: Force ON / Force OFF take the output over until
    /// Stop forcing; otherwise it follows the program's command.
    /// </summary>
    public sealed class DigitalOutputChannel
    {
        private readonly string _hp, _ph;
        private readonly HmiCommandButton _forceOn = new HmiCommandButton();
        private readonly HmiCommandButton _forceOff = new HmiCommandButton();
        private readonly HmiCommandButton _stopForcing = new HmiCommandButton();
        private bool _forcingActive, _value;

        public DigitalOutputChannel(DigitalOutputInfo info)
        {
            _hp = info.Hp;
            _ph = info.Ph;
        }

        /// <returns>The value driven to the field (pv).</returns>
        public bool Run(PlcTags t, bool cmd)
        {
            _forceOn.Run(t, _hp + "ForceOn", _ph + "ForceOn");
            _forceOff.Run(t, _hp + "ForceOff", _ph + "ForceOff");
            _stopForcing.Run(t, _hp + "StopForcing", _ph + "StopForcing");

            if (_forceOn.QR) { _forcingActive = true; _value = true; }
            if (_forceOff.QR) { _forcingActive = true; _value = false; }
            if (_stopForcing.QR) _forcingActive = false;
            if (!_forcingActive) _value = cmd;

            t.W(_ph + "Pv", _value);
            t.W(_ph + "ForcingActive", _forcingActive);
            return _value;
        }
    }
}
