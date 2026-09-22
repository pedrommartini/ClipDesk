namespace ClipDesk.Models;

public sealed class AppSettings
{
    public bool IsDarkMode { get; set; }
    public bool MagnetAlignmentEnabled { get; set; } = true;
    public double WorkspaceZoom { get; set; } = 1;
    public int ZoomScaleVersion { get; set; }
    public Dictionary<string, ClipDesk.Core.BoardViewportState> BoardViewports { get; set; } = [];
}
