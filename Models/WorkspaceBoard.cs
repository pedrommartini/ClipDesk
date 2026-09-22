using ClipDesk.Core;

namespace ClipDesk.Models;

public sealed class WorkspaceBoard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Mesa principal";
    public WorkspaceSyncMode SyncMode { get; set; } = WorkspaceSyncMode.Local;
    public string? OwnerId { get; set; }
    public double WorldWidth { get; set; } = BoardSpace.DefaultWidth;
    public double WorldHeight { get; set; } = BoardSpace.DefaultHeight;
    public int SchemaVersion { get; set; } = BoardSpace.SchemaVersion;
    public List<ClipboardItem> Items { get; set; } = [];
    public List<BoardObject> Objects { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public enum WorkspaceSyncMode { Local, PersonalCloud, Shared }
