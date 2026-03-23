using System.IO;
using System.Windows.Media;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace ToDoDo.Services;

public sealed class FontResolverService
{
    private static readonly string[] FontExtensions = new[] { ".ttf", ".otf", ".ttc" };

    public MediaFontFamily ResolvePreferredFontFamily()
    {
        foreach (var directory in GetCandidateFontDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var fontFile in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                         .Where(path => FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
            {
                var family = TryResolveFamily(fontFile);
                if (family is not null)
                {
                    return family;
                }
            }
        }

        return new MediaFontFamily("Pretendard, Malgun Gothic, Segoe UI");
    }

    private static IEnumerable<string> GetCandidateFontDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var fontsDirectory = Path.Combine(current.FullName, "fonts");
            if (seen.Add(fontsDirectory))
            {
                yield return fontsDirectory;
            }

            current = current.Parent;
        }
    }

    private static MediaFontFamily? TryResolveFamily(string fontFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(fontFilePath);
            if (string.IsNullOrWhiteSpace(directory)) return null;

            var folderUri = new Uri(directory.EndsWith(Path.DirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar);

            var families = Fonts.GetFontFamilies(folderUri);
            var preferred = families.FirstOrDefault(family => family.Source.Contains("Pretendard", StringComparison.OrdinalIgnoreCase));
            return preferred ?? families.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
