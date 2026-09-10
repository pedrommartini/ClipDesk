using System.Text.Json.Serialization;

namespace ClipDesk.Models;

public sealed class ClipboardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipboardItemType Type { get; set; }
    public string DisplayName { get; set; } = "Novo item";
    public string? Text { get; set; }
    public string? Url { get; set; }
    public List<string> FilePaths { get; set; } = [];
    public List<ClipboardItem> Children { get; set; } = [];
    public string? StoredFilePath { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsFileBacked => Type is ClipboardItemType.File or ClipboardItemType.Folder;

    [JsonIgnore]
    public bool IsClipDeskFolder => Type == ClipboardItemType.AppFolder;
}
