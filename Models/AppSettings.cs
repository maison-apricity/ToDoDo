namespace ToDoDo.Models;

public sealed class AppSettings
{
    public double Width { get; set; } = 940;
    public double Height { get; set; } = 860;
    public double Left { get; set; } = 60;
    public double Top { get; set; } = 50;
    public bool IsSidebarVisible { get; set; } = true;
    public bool IsPinnedToDesktop { get; set; } = true;
    public double SidebarWidth { get; set; } = 220;

    public int AutoArchiveDays { get; set; } = 0;
    public bool HideToTrayOnClose { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public TodoPriority DefaultPriority { get; set; } = TodoPriority.Normal;
    public TodoRepeat DefaultRepeat { get; set; } = TodoRepeat.None;
    public bool DefaultUseDueDate { get; set; } = false;
    public bool ShowCompletedStrikethrough { get; set; } = true;
}
