using System.Windows.Input;
using TerakkiFramework.Mvvm;

namespace SempaPumpStation.Screens
{
    /// <summary>One selectable option on a zone screen. <see cref="State"/> drives the Choice tile style.</summary>
    public sealed class ChoiceItem : ViewModelBase
    {
        public int Value { get; set; }
        public string Title { get; set; }
        public ICommand Command { get; set; }

        private string _subtitle = "";
        public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

        private string _state = "";
        /// <summary>"active" selected, "held" taken by another zone, "" free.</summary>
        public string State { get => _state; set => Set(ref _state, value); }
    }
}
