namespace ClipDesk.Plugin.FileConverter.Models;

public sealed record FormatDefinition(
    string Extension,
    string DisplayName,
    FileCategory Category,
    string MimeType,
    string IconGlyph,
    string Description,
    bool CanBeSource = true,
    bool CanBeTarget = true);
