namespace SempaPumpStation.Infrastructure.Plc
{
    /// <summary>
    /// Port of FB_Io_DigitalInput with FB_Io_DigitalInputHmi: Force ON / Force OFF hold the value the program sees
    /// until Stop forcing; otherwise it follows the field (the PLC's filter time is not simulated).
    /// </summary>
    public sealed class DigitalInputChannel
    {
        private readonly string _hp, _ph;
        private readonly HmiCommandButton _forceOn = new HmiCommandButton();
        private readonly HmiCommandButton _forceOff = new HmiCommandButton();
        private readonly HmiCommandButton _stopForcing = new HmiCommandButton();
        private bool _forcingActive, _value;

        public DigitalInputChannel(DigitalInputInfo info)
        {
            _hp = info.Hp;
            _ph = info.Ph;
        }

        /// <returns>The value the program works with (pv).</returns>
        public bool Run(PlcTags t, bool rawValue)
        {
            _forceOn.Run(t, _hp + "ForceOn", _ph + "ForceOn");
            _forceOff.Run(t, _hp + "ForceOff", _ph + "ForceOff");
            _stopForcing.Run(t, _hp + "StopForcing", _ph + "StopForcing");

            if (_forceOn.QR) { _forcingActive = true; _value = true; }
            if (_forceOff.QR) { _forcingActive = true; _value = false; }
            if (_stopForcing.QR) _forcingActive = false;
            if (!_forcingActive) _value = rawValue;

            t.W(_ph + "RawValue", rawValue);
            t.W(_ph + "Pv", _value);
            t.W(_ph + "ForcingActive", _forcingActive);
            return _value;
        }
    }
}
