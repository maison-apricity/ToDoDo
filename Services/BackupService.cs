using System.IO;
using System.Text.Json;
using ToDoDo.Models;

namespace ToDoDo.Services;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Export(string filePath, IEnumerable<TaskGroup> groups, IEnumerable<TodoItem> items, WidgetSettings settings)
    {
        var bundle = new BackupBundle
        {
            Version = "1.0",
            ExportedAt = DateTime.Now,
            Settings = settings,
            Groups = groups.OrderBy(group => group.SortOrder).ToList(),
            Items = items.OrderBy(item => item.GroupId).ThenBy(item => item.SortOrder).ToList()
        };

        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public BackupBundle? Import(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<BackupBundle>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
