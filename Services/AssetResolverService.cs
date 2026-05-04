using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ToDoDo.Services
{
    public static class AssetResolverService
    {
        private const string WindowIconUri = "pack://application:,,,/assets/ToDoDo.ico";
        private const string HeaderImageUri = "pack://application:,,,/assets/ToDoDo.png";

        // MainWindow.xaml.cs에서 기대하는 메서드명 유지
        public static BitmapFrame? ResolveWindowIcon()
        {
            return TryLoadWindowIcon();
        }

        public static BitmapSource? ResolveHeaderImage()
        {
            return TryLoadBitmapResource(HeaderImageUri) ?? TryLoadWindowIcon();
        }

        public static System.Drawing.Icon? ResolveTrayIcon()
        {
            return TryLoadNotifyIcon();
        }

        // 기존 호환용 메서드도 유지
        public static BitmapFrame? TryLoadWindowIcon()
        {
            try
            {
                var info = Application.GetResourceStream(new Uri(WindowIconUri, UriKind.Absolute));
                if (info?.Stream == null)
                {
                    return null;
                }

                using var stream = info.Stream;
                var frame = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
            catch
            {
                return null;
            }
        }

        public static System.Drawing.Icon? TryLoadNotifyIcon()
        {
            try
            {
                var info = Application.GetResourceStream(new Uri(WindowIconUri, UriKind.Absolute));
                if (info?.Stream == null)
                {
                    return null;
                }

                using var stream = info.Stream;
                using var icon = new System.Drawing.Icon(stream);
                return (System.Drawing.Icon)icon.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource? TryLoadBitmapResource(string packUri)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(packUri, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
