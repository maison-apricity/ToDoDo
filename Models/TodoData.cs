namespace ToDoDo.Models;

public sealed class TodoData
{
    public List<TaskGroup> Groups { get; set; } = new();
    public List<TodoItem> Items { get; set; } = new();
}
