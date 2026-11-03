using System.Windows;

namespace SempaPumpStation.Shell
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
        }

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Max_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
