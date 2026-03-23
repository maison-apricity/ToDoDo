using Forms = System.Windows.Forms;

namespace ToDoDo.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public event EventHandler? ShowRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleLockRequested;

    public TrayIconService()
    {
        var contextMenu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("복원");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var hideItem = new Forms.ToolStripMenuItem("숨기기");
        hideItem.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);

        var lockItem = new Forms.ToolStripMenuItem("위치 고정 / 해제");
        lockItem.Click += (_, _) => ToggleLockRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(hideItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(lockItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = AppIconFactory.CreateIcon(),
            Text = "ToDoDo",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
