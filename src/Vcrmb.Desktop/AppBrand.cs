using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Vcrmb.Desktop
{
    internal static class AppBrand
    {
        private static ImageSource image;
        internal static ImageSource Image
        {
            get
            {
                if (image == null)
                    using (Stream stream = Resource("Vcrmb.logo.png"))
                    {
                        BitmapImage bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
                    }
                return image;
            }
        }
        internal static Drawing.Icon TrayIcon()
        {
            using (Stream stream = Resource("Vcrmb.app.ico"))
            using (Drawing.Icon icon = new Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize))
                return (Drawing.Icon)icon.Clone();
        }
        private static Stream Resource(string name)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidDataException("程序图标缺失，请使用完整运行包。");
            return stream;
        }
    }
}
