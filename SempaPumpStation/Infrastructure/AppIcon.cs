using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>The Sempa logo (Assets), for the windows' icon and the headers.</summary>
    public static class AppIcon
    {
        public const string LogoUri = "pack://application:,,,/SempaPumpStation;component/Assets/sempa_logo.png";
        public const string IconUri = "pack://application:,,,/SempaPumpStation;component/Assets/sempa.ico";

        private static ImageSource _image;
        /// <summary>The window icon (all sizes of the logo).</summary>
        public static ImageSource Image => _image ?? (_image = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute)));
    }
}
