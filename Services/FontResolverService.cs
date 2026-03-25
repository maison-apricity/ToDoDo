using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace ToDoDo.Services
{
    public static class FontResolverService
    {
        private static readonly string[] PreferredFamilies =
        {
            "Pretendard",
            "Pretendard Variable",
            "Pretendard JP",
            "Pretendard GOV",
        };

        public static MediaFontFamily ResolvePreferredFontFamily()
        {
            var fontsDir = FindFontsDirectory();
            if (!string.IsNullOrWhiteSpace(fontsDir))
            {
                var family = TryResolvePretendardFromDirectory(fontsDir!);
                if (family != null)
                {
                    return family;
                }
            }

            return new MediaFontFamily("Malgun Gothic");
        }

        private static MediaFontFamily? TryResolvePretendardFromDirectory(string fontsDir)
        {
            var fontFiles = new List<string>();

            try
            {
                fontFiles.AddRange(Directory.EnumerateFiles(fontsDir, "*.ttf", SearchOption.AllDirectories));
                fontFiles.AddRange(Directory.EnumerateFiles(fontsDir, "*.otf", SearchOption.AllDirectories));
            }
            catch
            {
                return null;
            }

            if (fontFiles.Count == 0)
            {
                return null;
            }

            var pretendardFiles = fontFiles
                .Where(f => Path.GetFileName(f).Contains("Pretendard", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pretendardFiles.Count == 0)
            {
                return null;
            }

            foreach (var file in pretendardFiles)
            {
                var dir = Path.GetDirectoryName(file);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                var dirUri = new Uri(dir + Path.DirectorySeparatorChar);
                foreach (var familyName in PreferredFamilies)
                {
                    try
                    {
                        var family = new MediaFontFamily(dirUri, "./#" + familyName);
                        _ = family.FamilyNames.Count;
                        return family;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static string? FindFontsDirectory()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in EnumerateUpwardDirs(AppContext.BaseDirectory))
            {
                try
                {
                    foreach (var candidate in Directory.EnumerateDirectories(dir, "fonts", SearchOption.TopDirectoryOnly))
                    {
                        if (seen.Add(candidate))
                        {
                            return candidate;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
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
    }
}
