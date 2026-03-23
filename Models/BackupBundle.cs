using ToDoDo.Services;

namespace ToDoDo.Models;

public sealed class BackupBundle
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public WidgetSettings Settings { get; set; } = new();
    public List<TaskGroup> Groups { get; set; } = new();
    public List<TodoItem> Items { get; set; } = new();
}
