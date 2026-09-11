namespace ClipDesk.Models;

public sealed class AppSettings
{
    public bool IsDarkMode { get; set; }
    public double WorkspaceZoom { get; set; } = 1;
    public int ZoomScaleVersion { get; set; }
}
