namespace ClipDesk.Core;

/// <summary>
/// Stable document shape shared by present cards and future native board tools.
/// Content remains type-specific so attachments never need to be rewritten when
/// a visual property changes.
/// </summary>
public sealed class BoardObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceId { get; set; } = "";
    public BoardObjectKind Kind { get; set; } = BoardObjectKind.Card;
    public int SchemaVersion { get; set; } = 1;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public int ZIndex { get; set; }
    public bool Locked { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Style { get; set; } = [];
    public Dictionary<string, string> Content { get; set; } = [];
}

public enum BoardObjectKind
{
    Card,
    Text,
    Shape,
    Stroke,
    Connector,
    Group,
    StickyNote,
    Checklist,
    Calculator,
    Translator,
    CurrencyConverter
}

public interface IBoardTool
{
    string Id { get; }
}
