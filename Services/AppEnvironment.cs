using System.IO;

namespace ClipDesk.Services;

public static class AppEnvironment
{
#if CLIPDESK_DEV
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif
    public static int TestClientNumber
    {
        get
        {
            var args = Environment.GetCommandLineArgs().Skip(1);
            if (args.Any(arg => string.Equals(arg, "--test-client=1", StringComparison.OrdinalIgnoreCase))) return 1;
            if (args.Any(arg => string.Equals(arg, "--test-client=2", StringComparison.OrdinalIgnoreCase)
                || IsDevelopment && string.Equals(arg, "--second-client", StringComparison.OrdinalIgnoreCase))) return 2;
            return 0;
        }
    }
    public static bool IsTestClient => TestClientNumber != 0;
    public static string Name => IsTestClient ? $"ClipDesk{(IsDevelopment ? " DEV" : "")} · Teste {TestClientNumber}"
        : IsDevelopment ? "ClipDesk DEV" : "ClipDesk";
    public static string Identity => IsTestClient ? $"ClipDesk{(IsDevelopment ? ".Dev" : "")}.Test{TestClientNumber}"
        : IsDevelopment ? "ClipDesk.Dev" : "ClipDesk";
    public static string DataRoot => IsDevelopment && Environment.GetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT") is {Length:>0} root
        ? Path.GetFullPath(root) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            IsTestClient ? $"ClipDesk-{(IsDevelopment ? "Dev" : "Production")}-Test{TestClientNumber}"
                : IsDevelopment ? "ClipDesk-Dev" : "ClipDesk");

    // Optional plugins are user-visible. Keep DEV and test clients separate from the daily installation.
    public static string UserPluginsRoot
    {
        get
        {
            if (IsDevelopment && Environment.GetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT") is { Length: > 0 } root)
                return Path.Combine(Path.GetFullPath(root), "UserPlugins");
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var pluginRoot = Path.Combine(documents, "Clipdesk");
            if (IsTestClient) return Path.Combine(pluginRoot, IsDevelopment ? "Dev" : "Production", $"Test{TestClientNumber}");
            return IsDevelopment ? Path.Combine(pluginRoot, "Dev") : pluginRoot;
        }
    }
}
