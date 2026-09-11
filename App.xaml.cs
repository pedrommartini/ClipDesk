using System.Windows;
using ClipDesk.Services;

namespace ClipDesk;

public partial class App : Application
{
    private const string InstanceMutexName = "Local\\ClipDesk.SingleInstance";
    private const string ActivateEventName = "Local\\ClipDesk.ActivateExistingInstance";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activationRegistration;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Any(argument => string.Equals(argument, "--update", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunUpdaterAsync(e.Args);
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            using var activateExisting = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            activateExisting.Set();
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(ActivateExistingWindow),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        Exit += (_, _) => DisposeSingleInstanceResources();

        var startHidden = e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(startHidden);
        MainWindow = window;
        window.Show();
    }

    private void ActivateExistingWindow()
    {
        if (MainWindow is MainWindow window)
        {
            window.ActivateFromSecondaryLaunch();
        }
    }

    private void DisposeSingleInstanceResources()
    {
        _activationRegistration?.Unregister(null);
        _activateEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
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
