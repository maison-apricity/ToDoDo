namespace ToDoDo.Services;

public sealed class WidgetSettings
{
    public double Left { get; set; } = 18;
    public double Top { get; set; } = 18;
    public double Width { get; set; } = 860;
    public double Height { get; set; } = 600;
    public bool IsLocked { get; set; }
    public bool KeepBottomMost { get; set; } = true;
    public bool IsSidebarCollapsed { get; set; }
    public bool AutoCollapseCompleted { get; set; } = true;
    public bool EnableGlobalQuickAdd { get; set; } = true;
    public bool ShowCompletedSection { get; set; } = true;
    public string SelectedGroupId { get; set; } = string.Empty;
    public string SelectedFilter { get; set; } = "All";
}
