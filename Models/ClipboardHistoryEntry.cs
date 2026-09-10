using System.Text.Json.Serialization;

namespace ClipDesk.Models;

public sealed class ClipboardHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipboardItemType Type { get; set; }
    public string Title { get; set; } = "Item copiado";
    public string Preview { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Url { get; set; }
    public List<string> FilePaths { get; set; } = [];
    public string? StoredFilePath { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        ClipboardItemType.Text => "Texto",
        ClipboardItemType.Link => "Link",
        ClipboardItemType.Image => "Imagem",
        ClipboardItemType.File => "Arquivo",
        ClipboardItemType.Folder => "Pasta",
        ClipboardItemType.AppFolder => "Pasta ClipDesk",
        _ => "Item"
    };

    [JsonIgnore]
    public string SourceSummary
    {
        get
        {
            if (FilePaths.Count > 0)
            {
                return FilePaths.Count == 1 ? FilePaths[0] : $"{FilePaths.Count} caminhos copiados";
            }

            if (!string.IsNullOrWhiteSpace(StoredFilePath))
            {
                return StoredFilePath;
            }

            if (!string.IsNullOrWhiteSpace(Url))
            {
                return Url;
            }

            return Text?.Length > 0 ? $"{Text.Length} caracteres" : "Clipboard local";
        }
    }

    [JsonIgnore]
    public string Detail => Type switch
    {
        ClipboardItemType.Image => "Imagem preservada localmente para recópia rápida",
        ClipboardItemType.File or ClipboardItemType.Folder => FilePaths.Count == 1
            ? "Um caminho no clipboard"
            : $"{FilePaths.Count} caminhos no clipboard",
        ClipboardItemType.Link => "URL pronta para abrir ou colar",
        ClipboardItemType.Text => Text?.Contains('\n') == true ? "Texto com múltiplas linhas" : "Texto simples",
        _ => "Item armazenado no histórico"
    };
}
