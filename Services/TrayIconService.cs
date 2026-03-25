using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace ToDoDo.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(
        Action showAction,
        Action addAction,
        Action togglePinAction,
        Action exitAction,
        Func<bool> isPinnedProvider,
        Icon? customIcon = null)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "ToDoDo",
            Visible = true,
            Icon = customIcon ?? SystemIcons.Application
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => showAction());
        menu.Items.Add("추가", null, (_, _) => addAction());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var pinItem = new Forms.ToolStripMenuItem();
        void UpdatePinText() => pinItem.Text = isPinnedProvider() ? "바탕화면 고정 끄기" : "바탕화면 고정 켜기";
        UpdatePinText();
        pinItem.Click += (_, _) =>
        {
            togglePinAction();
            UpdatePinText();
        };
        menu.Items.Add(pinItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exitAction());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => showAction();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
