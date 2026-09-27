using ClipDesk.Plugin.FileConverter.Models;

namespace ClipDesk.Plugin.FileConverter.Services;

public static class FormatRegistry
{
    private static readonly Dictionary<string, FormatDefinition> Formats = new(StringComparer.OrdinalIgnoreCase);

    static FormatRegistry()
    {
        // ------------------ Imagens ------------------
        Register(new("png", "PNG (Imagem)", FileCategory.Image, "image/png", "\uEB9F", "Portable Network Graphics com transparência"));
        Register(new("jpg", "JPG / JPEG", FileCategory.Image, "image/jpeg", "\uEB9F", "JPEG comprimido de alta fidelidade"));
        Register(new("jpeg", "JPEG", FileCategory.Image, "image/jpeg", "\uEB9F", "JPEG comprimido", CanBeTarget: false));
        Register(new("webp", "WebP", FileCategory.Image, "image/webp", "\uEB9F", "Formato moderno de alta compressão web"));
        Register(new("bmp", "BMP (Bitmap)", FileCategory.Image, "image/bmp", "\uEB9F", "Bitmap sem perda"));
        Register(new("gif", "GIF", FileCategory.Image, "image/gif", "\uEB9F", "Graphics Interchange Format"));
        Register(new("ico", "ICO (Ícone / Favicon)", FileCategory.Image, "image/x-icon", "\uE749", "Ícone de aplicativo com multi-resolução"));
        Register(new("tiff", "TIFF", FileCategory.Image, "image/tiff", "\uEB9F", "Tagged Image File Format para impressão"));
        Register(new("tif", "TIF", FileCategory.Image, "image/tiff", "\uEB9F", "TIFF alternativo", CanBeTarget: false));
        Register(new("svg", "SVG (Vetorial)", FileCategory.Image, "image/svg+xml", "\uE91B", "Gráfico vetorial escalável"));

        // ------------------ Áudio ------------------
        Register(new("mp3", "MP3", FileCategory.Audio, "audio/mpeg", "\uE8D6", "Áudio comprimido universal"));
        Register(new("wav", "WAV", FileCategory.Audio, "audio/wav", "\uE8D6", "Áudio linear PCM sem perdas"));
        Register(new("aac", "AAC", FileCategory.Audio, "audio/aac", "\uE8D6", "Advanced Audio Coding de alta eficiência"));
        Register(new("wma", "WMA", FileCategory.Audio, "audio/x-ms-wma", "\uE8D6", "Windows Media Audio"));
        Register(new("m4a", "M4A", FileCategory.Audio, "audio/mp4", "\uE8D6", "Áudio MPEG-4"));
        Register(new("flac", "FLAC", FileCategory.Audio, "audio/flac", "\uE8D6", "Áudio lossless de estúdio"));
        Register(new("ogg", "OGG", FileCategory.Audio, "audio/ogg", "\uE8D6", "Ogg Vorbis"));

        // ------------------ Vídeo ------------------
        Register(new("mp4", "MP4", FileCategory.Video, "video/mp4", "\uE714", "MPEG-4 Vídeo universal"));
        Register(new("wmv", "WMV", FileCategory.Video, "video/x-ms-wmv", "\uE714", "Windows Media Video"));
        Register(new("avi", "AVI", FileCategory.Video, "video/x-msvideo", "\uE714", "Audio Video Interleave"));
        Register(new("mov", "MOV (QuickTime)", FileCategory.Video, "video/quicktime", "\uE714", "Vídeo Apple QuickTime"));
        Register(new("mkv", "MKV", FileCategory.Video, "video/x-matroska", "\uE714", "Matroska Multimedia"));
        Register(new("webm", "WebM", FileCategory.Video, "video/webm", "\uE714", "Vídeo aberto para web"));

        // ------------------ Dados ------------------
        Register(new("json", "JSON", FileCategory.Data, "application/json", "\uE943", "JavaScript Object Notation"));
        Register(new("csv", "CSV (Planilha / Tabela)", FileCategory.Data, "text/csv", "\uE80A", "Valores separados por vírgula"));
        Register(new("xml", "XML", FileCategory.Data, "application/xml", "\uE943", "Extensible Markup Language"));
        Register(new("yaml", "YAML", FileCategory.Data, "text/yaml", "\uE943", "YAML Ain't Markup Language"));
        Register(new("yml", "YML", FileCategory.Data, "text/yaml", "\uE943", "YAML alternativo", CanBeTarget: false));

        // ------------------ Documentos & Texto ------------------
        Register(new("pdf", "PDF", FileCategory.Document, "application/pdf", "\uEA90", "Portable Document Format"));
        Register(new("md", "Markdown (MD)", FileCategory.Document, "text/markdown", "\uE8A5", "Marcação legível moderna"));
        Register(new("markdown", "Markdown", FileCategory.Document, "text/markdown", "\uE8A5", "Markdown alternativo", CanBeTarget: false));
        Register(new("html", "HTML", FileCategory.Document, "text/html", "\uE774", "Hypertext Markup Language"));
        Register(new("htm", "HTM", FileCategory.Document, "text/html", "\uE774", "HTML alternativo", CanBeTarget: false));
        Register(new("txt", "TXT (Texto Puro)", FileCategory.Document, "text/plain", "\uE8A5", "Texto simples"));
        Register(new("rtf", "RTF", FileCategory.Document, "application/rtf", "\uE8A5", "Rich Text Format"));

        // ------------------ Arquivos Compactados ------------------
        Register(new("zip", "ZIP", FileCategory.Archive, "application/zip", "\uF012", "Arquivo compactado padrão"));
        Register(new("gz", "GZip", FileCategory.Archive, "application/gzip", "\uF012", "Gnu Zipped"));
        Register(new("tar", "TAR", FileCategory.Archive, "application/x-tar", "\uF012", "Tape Archive"));
    }

    private static void Register(FormatDefinition def)
    {
        Formats[def.Extension] = def;
    }

    public static FormatDefinition? GetFormat(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return Formats.TryGetValue(clean, out var def) ? def : null;
    }

    public static FileCategory DetectCategory(string extension)
    {
        var def = GetFormat(extension);
        return def?.Category ?? FileCategory.Unknown;
    }

    public static string GetCategoryDisplayName(FileCategory category) => category switch
    {
        FileCategory.Image => "Imagem",
        FileCategory.Audio => "Áudio",
        FileCategory.Video => "Vídeo",
        FileCategory.Data => "Dados & Planilhas",
        FileCategory.Document => "Documento & Texto",
        FileCategory.Archive => "Arquivo Compactado",
        _ => "Arquivo Geral"
    };

    public static string GetCategoryIconGlyph(FileCategory category) => category switch
    {
        FileCategory.Image => "\uEB9F",
        FileCategory.Audio => "\uE8D6",
        FileCategory.Video => "\uE714",
        FileCategory.Data => "\uE943",
        FileCategory.Document => "\uEA90",
        FileCategory.Archive => "\uF012",
        _ => "\uE8A5"
    };

    /// <summary>
    /// Retorna os formatos de destino estritamente compatíveis com base na categoria e regras semânticas.
    /// </summary>
    public static IReadOnlyList<FormatDefinition> GetCompatibleTargetFormats(string sourceExtension)
    {
        var clean = sourceExtension.TrimStart('.').ToLowerInvariant();
        var sourceDef = GetFormat(clean);
        var targets = new List<FormatDefinition>();

        if (sourceDef is null)
            return targets;

        switch (sourceDef.Category)
        {
            case FileCategory.Image:
                // Imagens convertem para outras imagens, PDF ou Base64 (TXT)
                AddTargets(targets, "png", "jpg", "webp", "ico", "bmp", "gif", "tiff", "pdf", "txt");
                break;

            case FileCategory.Audio:
                // Áudio só pode virar áudio
                AddTargets(targets, "mp3", "wav", "aac", "wma");
                break;

            case FileCategory.Video:
                // Vídeo pode virar outro formato de vídeo, extrair áudio (MP3/WAV) ou virar GIF animado
                AddTargets(targets, "mp4", "wmv", "avi", "mp3", "wav", "gif");
                break;

            case FileCategory.Data:
                // Dados convertem entre JSON, CSV, XML, YAML, TXT
                AddTargets(targets, "json", "csv", "xml", "yaml", "txt");
                break;

            case FileCategory.Document:
                // Documentos convertem para PDF, HTML, MD, TXT
                AddTargets(targets, "pdf", "html", "md", "txt");
                break;

            case FileCategory.Archive:
                AddTargets(targets, "zip", "gz", "tar");
                break;
        }

        // Remove o próprio formato de origem para não oferecer conversão inútil idêntica
        targets.RemoveAll(t => string.Equals(t.Extension, clean, StringComparison.OrdinalIgnoreCase));
        return targets;
    }

    private static void AddTargets(List<FormatDefinition> list, params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            if (Formats.TryGetValue(ext, out var def) && def.CanBeTarget && !list.Contains(def))
            {
                list.Add(def);
            }
        }
    }

    /// <summary>
    /// Validação semântica estrita: impede conversões incompatíveis (ex: imagem para áudio).
    /// </summary>
    public static bool IsConversionAllowed(string sourceExtension, string targetExtension, out string? errorMessage)
    {
        var src = sourceExtension.TrimStart('.').ToLowerInvariant();
        var tgt = targetExtension.TrimStart('.').ToLowerInvariant();

        if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "O formato de destino é idêntico ao formato de origem.";
            return false;
        }

        var srcDef = GetFormat(src);
        var tgtDef = GetFormat(tgt);

        if (srcDef is null)
        {
            errorMessage = $"Formato de origem '.{src}' não é suportado.";
            return false;
        }

        if (tgtDef is null)
        {
            errorMessage = $"Formato de destino '.{tgt}' não é suportado.";
            return false;
        }

        var allowedTargets = GetCompatibleTargetFormats(src);
        if (!allowedTargets.Any(t => string.Equals(t.Extension, tgt, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = $"Regra de compatibilidade violada: {GetCategoryDisplayName(srcDef.Category)} ({src.ToUpperInvariant()}) não pode ser convertido para {GetCategoryDisplayName(tgtDef.Category)} ({tgt.ToUpperInvariant()}).";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
