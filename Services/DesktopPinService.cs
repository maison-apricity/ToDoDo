using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ToDoDo.Services;

public sealed class DesktopPinService
{
    private readonly Window _window;

    public DesktopPinService(Window window)
    {
        _window = window;
    }

    public bool IsPinned { get; private set; }

    public void SetPinned(bool pinned)
    {
        IsPinned = pinned;
        Apply();
    }

    public void Reapply()
    {
        if (IsPinned)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (IsPinned)
        {
            SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        else
        {
            SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
