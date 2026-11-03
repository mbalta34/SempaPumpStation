using System;
using System.Windows.Threading;
using TerakkiUI.Components.User;

namespace SempaPumpStation.Shell
{
    public partial class LoginWindow : ModalOverlayWindow
    {
        public LoginWindow()
        {
            InitializeComponent();

            // BindablePasswordBox takes the focus when it loads; give it to the user name instead while
            // that is still empty. Queued after the Loaded handlers so it wins.
            Loaded += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (string.IsNullOrEmpty(UsernameBox.Text)) UsernameBox.Focus();
                else PasswordField.FocusPassword();
            }));
        }
    }
}
