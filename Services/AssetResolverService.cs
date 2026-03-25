using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace ToDoDo.Services
{
    public static class AssetResolverService
    {
        // MainWindow.xaml.cs에서 기대하는 메서드명 유지
        public static BitmapFrame? ResolveWindowIcon()
        {
            return TryLoadWindowIcon();
        }

        public static System.Drawing.Icon? ResolveTrayIcon()
        {
            return TryLoadNotifyIcon();
        }

        // 기존 호환용 메서드도 유지
        public static BitmapFrame? TryLoadWindowIcon()
        {
            var path = FindBestIcoPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(path);
                return BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
            }
            catch
            {
                return null;
            }
        }

        public static System.Drawing.Icon? TryLoadNotifyIcon()
        {
            var path = FindBestIcoPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(path);
                return new System.Drawing.Icon(stream);
            }
            catch
            {
                return null;
            }
        }

        public static string? FindBestIcoPath()
        {
            var assetDirs = EnumerateCandidateAssetDirs()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (assetDirs.Count == 0)
            {
                return null;
            }

            var files = new List<string>();

            foreach (var dir in assetDirs)
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(dir, "*.ico", SearchOption.AllDirectories));
                }
                catch
                {
                }
            }

            if (files.Count == 0)
            {
                return null;
            }

            return files
                .OrderBy(ScoreIconPath)
                .ThenBy(p => p.Length)
                .FirstOrDefault();
        }

        private static IEnumerable<string> EnumerateCandidateAssetDirs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in EnumerateUpwardDirs(AppContext.BaseDirectory))
            {
                IEnumerable<string> candidates = Array.Empty<string>();

                try
                {
                    candidates = Directory.EnumerateDirectories(dir, "assets", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var candidate in candidates)
                {
                    if (seen.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateUpwardDirs(string start)
        {
            var current = new DirectoryInfo(start);
            while (current != null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }

        private static int ScoreIconPath(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();

            if (name == "app.ico") return 0;
            if (name == "tododo.ico") return 1;
            if (name == "icon.ico") return 2;
            if (name.Contains("app")) return 10;
            if (name.Contains("tododo")) return 11;
            if (name.Contains("icon")) return 12;
            return 100;
        }
    }
}
