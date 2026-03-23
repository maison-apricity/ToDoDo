using System.IO;
using System.Text.Json;

namespace ToDoDo.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public WidgetSettings Load()
    {
        FilePaths.EnsureAppDirectory();

        if (!File.Exists(FilePaths.SettingsFilePath))
        {
            return new WidgetSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePaths.SettingsFilePath);
            return JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions) ?? new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public void Save(WidgetSettings settings)
    {
        FilePaths.EnsureAppDirectory();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePaths.SettingsFilePath, json);
    }
}
