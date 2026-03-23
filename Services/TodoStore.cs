using System.IO;
using System.Text.Json;
using ToDoDo.Models;

namespace ToDoDo.Services;

public sealed class TodoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TodoData Load()
    {
        FilePaths.EnsureAppDirectory();

        if (!File.Exists(FilePaths.DataFilePath))
        {
            return new TodoData();
        }

        try
        {
            var json = File.ReadAllText(FilePaths.DataFilePath);
            return JsonSerializer.Deserialize<TodoData>(json, JsonOptions) ?? new TodoData();
        }
        catch
        {
            return new TodoData();
        }
    }

    public void Save(IEnumerable<TaskGroup> groups, IEnumerable<TodoItem> items)
    {
        FilePaths.EnsureAppDirectory();

        var data = new TodoData
        {
            Groups = groups.OrderBy(group => group.SortOrder).ToList(),
            Items = items.OrderBy(item => item.GroupId).ThenBy(item => item.SortOrder).ToList()
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(FilePaths.DataFilePath, json);
    }
}
