namespace ClipDesk.Models;

public sealed class WorkspaceBoard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Mesa principal";
    public List<ClipboardItem> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
