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
}
