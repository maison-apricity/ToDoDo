using System.Windows.Threading;

namespace ToDoDo.Services;

public sealed class BottomMostService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _enabled;

    public BottomMostService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += (_, _) =>
        {
            if (_enabled)
            {
                ApplyBottomMost();
            }
        };
    }

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        ApplyWindowStyle();
    }

    public void Start()
    {
        _enabled = true;
        ApplyBottomMost();
        _timer.Start();
    }

    public void Stop()
    {
        _enabled = false;
        _timer.Stop();
    }

    private void ApplyWindowStyle()
    {
        if (_hwnd == IntPtr.Zero) return;

        var exStyle = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE).ToInt64();
        exStyle |= Win32.WS_EX_TOOLWINDOW;
        exStyle &= ~Win32.WS_EX_APPWINDOW;

        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    public void ApplyBottomMost()
    {
        if (_hwnd == IntPtr.Zero) return;

        Win32.SetWindowPos(
            _hwnd,
            Win32.HWND_BOTTOM,
            0,
            0,
            0,
            0,
            Win32.SWP_NOMOVE |
            Win32.SWP_NOSIZE |
            Win32.SWP_NOACTIVATE |
            Win32.SWP_NOOWNERZORDER |
            Win32.SWP_NOSENDCHANGING);
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
