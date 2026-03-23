using System.IO;

namespace ToDoDo.Services;

public static class FilePaths
{
    private static readonly string AppDirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToDoDo");

    public static string DataFilePath => Path.Combine(AppDirectoryPath, "data.json");
    public static string SettingsFilePath => Path.Combine(AppDirectoryPath, "settings.json");

    public static void EnsureAppDirectory()
    {
        Directory.CreateDirectory(AppDirectoryPath);
    }
}
