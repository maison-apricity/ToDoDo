using System.Runtime.InteropServices;
using System.Windows;
using ToDoDo.Services;

namespace ToDoDo;

public partial class App : System.Windows.Application
{
    private const string AppUserModelId = "ToDoDo.Desktop.TaskManager";
    private MainWindow? _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        TrySetAppUserModelId();
        Resources["AppFontFamily"] = FontResolverService.ResolvePreferredFontFamily();
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.DisposeServices();
        }
    }

    private static void TrySetAppUserModelId()
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Taskbar identity is a visual integration aid; startup must not fail if Windows rejects it.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
