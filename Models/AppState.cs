namespace ToDoDo.Models;

public sealed class AppState
{
    public List<TaskGroup> Groups { get; set; } = new();
    public List<TodoItem> Todos { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
