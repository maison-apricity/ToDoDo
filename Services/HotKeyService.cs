namespace ToDoDo.Services;

public sealed class HotKeyService : IDisposable
{
    private const int QuickAddHotKeyId = 0x2451;
    private readonly int _modifiers = Win32.MOD_CONTROL | Win32.MOD_ALT;
    private readonly uint _virtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.T);
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _registered;

    public event EventHandler? QuickAddRequested;

    public void Initialize(IntPtr hwnd, bool enabled)
    {
        _hwnd = hwnd;
        if (enabled)
        {
            Register();
        }
    }

    public void UpdateRegistration(bool enabled)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (enabled)
        {
            Register();
        }
        else
        {
            Unregister();
        }
    }

    private void Register()
    {
        if (_registered || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _registered = Win32.RegisterHotKey(_hwnd, QuickAddHotKeyId, _modifiers, _virtualKey);
    }

    private void Unregister()
    {
        if (!_registered || _hwnd == IntPtr.Zero)
        {
            return;
        }

        Win32.UnregisterHotKey(_hwnd, QuickAddHotKeyId);
        _registered = false;
    }

    public bool TryHandleHotKey(IntPtr wParam)
    {
        if (wParam.ToInt32() != QuickAddHotKeyId)
        {
            return false;
        }

        QuickAddRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        Unregister();
    }
}
