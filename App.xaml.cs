using System.Windows;
using ToDoDo.Services;

namespace ToDoDo;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
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
}
