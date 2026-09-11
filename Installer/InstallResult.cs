namespace ClipDesk.Installer;

public sealed record InstallResult(string InstallPath, string ExecutablePath, int FileCount);
public sealed record InstallProgress(double Value, string Message);
