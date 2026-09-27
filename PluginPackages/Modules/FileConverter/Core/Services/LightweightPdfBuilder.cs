using System.Globalization;
using System.Text;

namespace ClipDesk.Plugin.FileConverter.Services;

/// <summary>
/// Construtor de PDF 1.4 canônico ultraleve e puro em .NET, sem dependências externas.
/// Produz arquivos PDF válidos para documentos de texto e imagens.
/// </summary>
public static class LightweightPdfBuilder
{
    private const double PageWidth = 595.28;  // A4 largura em pontos (72 dpi)
    private const double PageHeight = 841.89; // A4 altura em pontos
    private const double Margin = 40.0;

    public static byte[] CreateTextPdf(string text, string title = "Documento")
    {
        var lines = WrapText(text, 80);
        const double fontSize = 11.0;
        const double leading = 15.0;
        int linesPerPage = (int)((PageHeight - (Margin * 2) - 40) / leading);
        if (linesPerPage <= 0) linesPerPage = 40;

        var pages = new List<List<string>>();
        for (int i = 0; i < lines.Count; i += linesPerPage)
        {
            pages.Add(lines.Skip(i).Take(linesPerPage).ToList());
        }
        if (pages.Count == 0) pages.Add(new() { "" });

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true);

        var offsets = new List<long>();
        offsets.Add(0); // Dummy para 1-indexed

        void WriteObjHeader(int objNum)
        {
            writer.Flush();
            offsets.Add(ms.Position);
            writer.WriteLine($"{objNum} 0 obj");
        }

        // Header
        writer.WriteLine("%PDF-1.4");
        writer.WriteLine("%\xE2\xE3\xCF\xD3");

        int catalogObj = 1;
        int pagesObj = 2;
        int fontObj = 3;
        int nextObj = 4;

        var pageObjNums = new List<int>();
        var contentObjNums = new List<int>();

        for (int i = 0; i < pages.Count; i++)
        {
            pageObjNums.Add(nextObj++);
            contentObjNums.Add(nextObj++);
        }

        // 1: Catalog
        WriteObjHeader(catalogObj);
        writer.WriteLine($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        writer.WriteLine("endobj");

        // 2: Pages
        WriteObjHeader(pagesObj);
        writer.WriteLine("<< /Type /Pages");
        writer.WriteLine($"   /Kids [{string.Join(" ", pageObjNums.Select(n => $"{n} 0 R"))}]");
        writer.WriteLine($"   /Count {pages.Count}");
        writer.WriteLine(">>");
        writer.WriteLine("endobj");

        // 3: Font
        WriteObjHeader(fontObj);
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");

        // Pages and Contents
        for (int i = 0; i < pages.Count; i++)
        {
            int pNum = pageObjNums[i];
            int cNum = contentObjNums[i];

            // Page obj
            WriteObjHeader(pNum);
            writer.WriteLine("<< /Type /Page");
            writer.WriteLine($"   /Parent {pagesObj} 0 R");
            writer.WriteLine($"   /MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}]");
            writer.WriteLine($"   /Contents {cNum} 0 R");
            writer.WriteLine($"   /Resources << /Font << /F1 {fontObj} 0 R >> >>");
            writer.WriteLine(">>");
            writer.WriteLine("endobj");

            // Content stream
            var contentSb = new StringBuilder();
            contentSb.AppendLine("BT");
            contentSb.AppendLine($"/F1 {fontSize.ToString("F1", CultureInfo.InvariantCulture)} Tf");

            // Título na primeira página
            double yStart = PageHeight - Margin;
            if (i == 0 && !string.IsNullOrWhiteSpace(title))
            {
                contentSb.AppendLine($"/F1 16 Tf");
                contentSb.AppendLine($"1 0 0 1 {Margin:F2} {(PageHeight - Margin):F2} Tm");
                contentSb.AppendLine($"({EscapePdfString(title)}) Tj");
                contentSb.AppendLine($"/F1 {fontSize.ToString("F1", CultureInfo.InvariantCulture)} Tf");
                yStart -= 30;
            }

            contentSb.AppendLine($"1 0 0 1 {Margin:F2} {yStart:F2} Tm");
            contentSb.AppendLine($"{leading.ToString("F1", CultureInfo.InvariantCulture)} TL");

            foreach (var l in pages[i])
            {
                contentSb.AppendLine($"({EscapePdfString(l)}) '");
            }

            // Rodapé com número de página
            contentSb.AppendLine($"/F1 9 Tf");
            contentSb.AppendLine($"1 0 0 1 {(PageWidth - Margin - 60):F2} {(Margin / 2):F2} Tm");
            contentSb.AppendLine($"(P\xE1gina {i + 1} de {pages.Count}) Tj");
            contentSb.AppendLine("ET");

            var contentBytes = Encoding.Latin1.GetBytes(contentSb.ToString());

            WriteObjHeader(cNum);
            writer.WriteLine($"<< /Length {contentBytes.Length} >>");
            writer.WriteLine("stream");
            writer.Flush();
            ms.Write(contentBytes, 0, contentBytes.Length);
            writer.WriteLine();
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");
        }

        // XRef
        writer.Flush();
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {nextObj}");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i < nextObj; i++)
        {
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        }

        // Trailer
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {nextObj} /Root {catalogObj} 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos);
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    public static byte[] CreateImagePdf(byte[] imageBytes, string imageFormat, int imageWidth, int imageHeight)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true);

        var offsets = new List<long> { 0 };

        void WriteObj(int objNum)
        {
            writer.Flush();
            offsets.Add(ms.Position);
            writer.WriteLine($"{objNum} 0 obj");
        }

        writer.WriteLine("%PDF-1.4");
        writer.WriteLine("%\xE2\xE3\xCF\xD3");

        // Dimensões da imagem na página mantendo proporção
        double maxWidth = PageWidth - (Margin * 2);
        double maxHeight = PageHeight - (Margin * 2);
        double scale = Math.Min(maxWidth / imageWidth, maxHeight / imageHeight);
        double renderWidth = imageWidth * scale;
        double renderHeight = imageHeight * scale;
        double xPos = Margin + ((maxWidth - renderWidth) / 2);
        double yPos = Margin + ((maxHeight - renderHeight) / 2);

        // 1: Catalog
        WriteObj(1);
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");

        // 2: Pages
        WriteObj(2);
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");

        // 3: Page
        WriteObj(3);
        writer.WriteLine("<< /Type /Page");
        writer.WriteLine("   /Parent 2 0 R");
        writer.WriteLine($"   /MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}]");
        writer.WriteLine("   /Contents 4 0 R");
        writer.WriteLine("   /Resources << /XObject << /Im1 5 0 R >> >>");
        writer.WriteLine(">>");
        writer.WriteLine("endobj");

        // 4: Content Stream
        var content = $"q {renderWidth:F2} 0 0 {renderHeight:F2} {xPos:F2} {yPos:F2} cm /Im1 Do Q";
        var contentBytes = Encoding.ASCII.GetBytes(content);
        WriteObj(4);
        writer.WriteLine($"<< /Length {contentBytes.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        ms.Write(contentBytes, 0, contentBytes.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        // 5: Image XObject
        bool isJpeg = imageFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                     imageFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase);

        WriteObj(5);
        writer.WriteLine("<< /Type /XObject /Subtype /Image");
        writer.WriteLine($"   /Width {imageWidth} /Height {imageHeight}");
        writer.WriteLine("   /ColorSpace /DeviceRGB /BitsPerComponent 8");
        if (isJpeg)
        {
            writer.WriteLine("   /Filter /DCTDecode");
        }
        writer.WriteLine($"   /Length {imageBytes.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        ms.Write(imageBytes, 0, imageBytes.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        // XRef & Trailer
        writer.Flush();
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
        {
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        }
        writer.WriteLine("trailer");
        writer.WriteLine("<< /Size 6 /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos);
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static string EscapePdfString(string input)
    {
        return input.Replace("\\", "\\\\")
                    .Replace("(", "\\(")
                    .Replace(")", "\\)")
                    .Replace("\r", "")
                    .Replace("\n", " ");
    }

    private static List<string> WrapText(string text, int maxCharsPerLine)
    {
        var result = new List<string>();
        var paragraphs = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var p in paragraphs)
        {
            if (p.Length <= maxCharsPerLine)
            {
                result.Add(p);
                continue;
            }

            var words = p.Split(' ');
            var currentLine = new StringBuilder();

            foreach (var w in words)
            {
                if (currentLine.Length + w.Length + 1 <= maxCharsPerLine)
                {
                    if (currentLine.Length > 0) currentLine.Append(' ');
                    currentLine.Append(w);
                }
                else
                {
                    if (currentLine.Length > 0)
                    {
                        result.Add(currentLine.ToString());
                        currentLine.Clear();
                    }
                    currentLine.Append(w);
                }
            }

            if (currentLine.Length > 0)
                result.Add(currentLine.ToString());
        }

        return result;
    }
}
