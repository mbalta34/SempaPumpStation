using System.ComponentModel;
using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Shell
{
    public sealed class NavEntry : INotifyPropertyChanged
    {
        public string Key { get; set; }
        public string Icon { get; set; }
        public string Label { get; set; }
        public ScreenBase Screen { get; set; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(TagState)); } }
        }

        public string TagState => _isActive ? "active" : "";

        private bool _isVisible = true;
        /// <summary>False hides the entry, e.g. user management for anyone but an administrator.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
