using System;
using System.Windows.Threading;
using TerakkiUI.Components.User;

namespace SempaPumpStation.Shell
{
    public partial class ChangePasswordWindow : ModalOverlayWindow
    {
        public ChangePasswordWindow()
        {
            InitializeComponent();

            // Each BindablePasswordBox focuses itself on load, so the last one would win; start at the first
            Loaded += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => CurrentBox.FocusPassword()));
        }
    }
}
