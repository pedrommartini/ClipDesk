using System.Windows;

namespace ClipDesk;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var startHidden = e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startHidden);
        MainWindow = window;
        window.Show();
    }
}
