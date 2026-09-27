using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ClipDesk.Plugin.FileConverter.Services;

public static class DataDocumentConverter
{
    // =========================================================================
    // JSON <-> CSV
    // =========================================================================

    public static string JsonToCsv(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        List<JsonElement> elements = new();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    elements.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            elements.Add(root);
        }
        else
        {
            throw new InvalidOperationException("O JSON deve conter um objeto ou uma lista de objetos para converter em CSV.");
        }

        if (elements.Count == 0)
            return string.Empty;

        // Extrai todas as propriedades únicas para os cabeçalhos
        var headers = new List<string>();
        foreach (var el in elements)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (!headers.Contains(prop.Name))
                    headers.Add(prop.Name);
            }
        }

        var sb = new StringBuilder();
        // Linha de cabeçalho
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsvValue)));

        // Linhas de dados
        foreach (var el in elements)
        {
            var row = new List<string>();
            foreach (var h in headers)
            {
                if (el.TryGetProperty(h, out var val))
                {
                    row.Add(EscapeCsvValue(val.ToString()));
                }
                else
                {
                    row.Add("");
                }
            }
            sb.AppendLine(string.Join(",", row));
        }

        return sb.ToString();
    }

    public static string CsvToJson(string csv, bool prettyPrint = true)
    {
        var lines = csv.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return "[]";

        var headers = ParseCsvLine(lines[0]);
        var list = new List<Dictionary<string, string>>();

        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            var dict = new Dictionary<string, string>();
            for (int j = 0; j < headers.Count; j++)
            {
                var val = j < values.Count ? values[j] : "";
                dict[headers[j]] = val;
            }
            list.Add(dict);
        }

        var options = new JsonSerializerOptions { WriteIndented = prettyPrint };
        return JsonSerializer.Serialize(list, options);
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // Pula aspa dupla escapada
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    // =========================================================================
    // JSON <-> XML
    // =========================================================================

    public static string JsonToXml(string json, string rootElementName = "root")
    {
        using var doc = JsonDocument.Parse(json);
        var xRoot = new XElement(rootElementName);
        BuildXmlElement(doc.RootElement, xRoot, "item");
        return xRoot.ToString();
    }

    private static void BuildXmlElement(JsonElement json, XElement parent, string defaultName)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in json.EnumerateObject())
                {
                    var child = new XElement(SanitizeXmlTag(prop.Name));
                    BuildXmlElement(prop.Value, child, "item");
                    parent.Add(child);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in json.EnumerateArray())
                {
                    var child = new XElement(defaultName);
                    BuildXmlElement(item, child, "item");
                    parent.Add(child);
                }
                break;

            default:
                parent.Value = json.ToString();
                break;
        }
    }

    private static string SanitizeXmlTag(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }
        if (sb.Length == 0 || char.IsDigit(sb[0]))
            sb.Insert(0, "item_");
        return sb.ToString();
    }

    public static string XmlToJson(string xml, bool prettyPrint = true)
    {
        var xDoc = XDocument.Parse(xml);
        var jsonNode = ConvertXmlToJsonNode(xDoc.Root!);
        var options = new JsonSerializerOptions { WriteIndented = prettyPrint };
        return jsonNode.ToJsonString(options);
    }

    private static JsonNode ConvertXmlToJsonNode(XElement element)
    {
        if (!element.HasElements)
        {
            return JsonValue.Create(element.Value)!;
        }

        var obj = new JsonObject();
        var groups = element.Elements().GroupBy(e => e.Name.LocalName);

        foreach (var group in groups)
        {
            if (group.Count() > 1)
            {
                var arr = new JsonArray();
                foreach (var item in group)
                {
                    arr.Add(ConvertXmlToJsonNode(item));
                }
                obj[group.Key] = arr;
            }
            else
            {
                obj[group.Key] = ConvertXmlToJsonNode(group.First());
            }
        }
        return obj;
    }

    // =========================================================================
    // JSON <-> YAML
    // =========================================================================

    public static string JsonToYaml(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        BuildYaml(doc.RootElement, sb, 0, false);
        return sb.ToString();
    }

    private static void BuildYaml(JsonElement element, StringBuilder sb, int indent, bool inline)
    {
        string indentStr = new(' ', indent);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (!inline && indent > 0) sb.AppendLine();
                foreach (var prop in element.EnumerateObject())
                {
                    sb.Append(indentStr).Append(prop.Name).Append(':');
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine();
                        BuildYaml(prop.Value, sb, indent + 2, false);
                    }
                    else
                    {
                        sb.Append(' ');
                        BuildYaml(prop.Value, sb, indent + 2, true);
                    }
                }
                break;

            case JsonValueKind.Array:
                if (!inline && indent > 0) sb.AppendLine();
                foreach (var item in element.EnumerateArray())
                {
                    sb.Append(indentStr).Append("- ");
                    BuildYaml(item, sb, indent + 2, true);
                }
                break;

            case JsonValueKind.String:
                string str = element.GetString() ?? "";
                if (str.Contains('\n') || str.Contains(':') || str.Contains('#') || str.StartsWith(' '))
                    sb.AppendLine($"\"{str.Replace("\"", "\\\"")}\"");
                else
                    sb.AppendLine(str);
                break;

            case JsonValueKind.True:
                sb.AppendLine("true");
                break;
            case JsonValueKind.False:
                sb.AppendLine("false");
                break;
            case JsonValueKind.Null:
                sb.AppendLine("null");
                break;
            case JsonValueKind.Number:
                sb.AppendLine(element.GetRawText());
                break;
        }
    }

    // =========================================================================
    // Markdown <-> HTML / Text
    // =========================================================================

    public static string MarkdownToHtml(string markdown, string title = "Documento Convertido")
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"pt-BR\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine($"  <title>{EscapeHtml(title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; color: #1e293b; max-width: 800px; margin: 40px auto; padding: 0 20px; }");
        sb.AppendLine("    h1, h2, h3, h4 { color: #0f172a; margin-top: 24px; margin-bottom: 12px; }");
        sb.AppendLine("    h1 { border-bottom: 2px solid #e2e8f0; padding-bottom: 8px; }");
        sb.AppendLine("    code { background: #f1f5f9; padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; font-size: 0.9em; }");
        sb.AppendLine("    pre { background: #0f172a; color: #f8fafc; padding: 16px; border-radius: 8px; overflow-x: auto; }");
        sb.AppendLine("    pre code { background: transparent; color: inherit; padding: 0; }");
        sb.AppendLine("    blockquote { border-left: 4px solid #6366f1; margin: 0; padding-left: 16px; color: #64748b; font-style: italic; }");
        sb.AppendLine("    hr { border: none; border-top: 1px solid #e2e8f0; margin: 32px 0; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 16px 0; }");
        sb.AppendLine("    th, td { border: 1px solid #e2e8f0; padding: 8px 12px; text-align: left; }");
        sb.AppendLine("    th { background: #f8fafc; font-weight: 600; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inCodeBlock = false;
        bool inList = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    sb.AppendLine("</code></pre>");
                    inCodeBlock = false;
                }
                else
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    sb.AppendLine("<pre><code>");
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine(EscapeHtml(line));
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                continue;
            }

            // Headers
            if (trimmed.StartsWith("### "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h3>{FormatInlineMarkdown(trimmed[4..])}</h3>");
            }
            else if (trimmed.StartsWith("## "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h2>{FormatInlineMarkdown(trimmed[3..])}</h2>");
            }
            else if (trimmed.StartsWith("# "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<h1>{FormatInlineMarkdown(trimmed[2..])}</h1>");
            }
            else if (trimmed.StartsWith("> "))
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<blockquote>{FormatInlineMarkdown(trimmed[2..])}</blockquote>");
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                sb.AppendLine($"  <li>{FormatInlineMarkdown(trimmed[2..])}</li>");
            }
            else if (trimmed == "---" || trimmed == "***")
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine("<hr />");
            }
            else
            {
                if (inList) { sb.AppendLine("</ul>"); inList = false; }
                sb.AppendLine($"<p>{FormatInlineMarkdown(trimmed)}</p>");
            }
        }

        if (inCodeBlock) sb.AppendLine("</code></pre>");
        if (inList) sb.AppendLine("</ul>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string FormatInlineMarkdown(string text)
    {
        var result = EscapeHtml(text);
        // Bold: **text**
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        // Italic: *text*
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*(.+?)\*", "<em>$1</em>");
        // Inline code: `text`
        result = System.Text.RegularExpressions.Regex.Replace(result, @"`(.+?)`", "<code>$1</code>");
        return result;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    public static string HtmlToText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<script[^>]*>[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</p>", "\n\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}
