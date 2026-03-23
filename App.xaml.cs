using System.Windows;
using ToDoDo.Services;

namespace ToDoDo;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var fontResolver = new FontResolverService();
        Resources["AppFontFamily"] = fontResolver.ResolvePreferredFontFamily();

        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }
}
