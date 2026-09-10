using System.Windows;
using ClipDesk.Services;

namespace ClipDesk;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Any(argument => string.Equals(argument, "--update", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunUpdaterAsync(e.Args);
            return;
        }

        var startHidden = e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startHidden);
        MainWindow = window;
        window.Show();
    }

    private async Task RunUpdaterAsync(string[] args)
    {
        try
        {
            await UpdateService.RunUpdaterAsync(args);
        }
        finally
        {
            Shutdown();
        }
    }
}
