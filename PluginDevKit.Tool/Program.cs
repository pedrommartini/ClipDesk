using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Xml.Linq;
using ClipDesk.PluginSdk;

if (args.Length == 2 && args[0] == "--schema")
{
    var schema = ManifestSchema(typeof(PluginManifest));
    schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
    schema["title"] = "ClipDesk plugin manifest v3";
    schema["required"] = new JsonArray("manifestVersion", "id", "name", "version", "runtime", "pluginApiVersion",
        "contractVersion", "stateVersion", "module", "platforms", "renderers");
    var properties = (JsonObject)schema["properties"]!;
    properties["manifestVersion"] = new JsonObject { ["const"] = 3 };
    properties["pluginApiVersion"] = new JsonObject { ["const"] = 3 };
    properties["runtime"] = new JsonObject { ["const"] = PluginRuntimes.PortableV3 };
    properties["contractVersion"] = new JsonObject { ["type"] = "string", ["pattern"] = "^3\\.[0-9]+\\.[0-9]+$" };
    File.WriteAllText(Path.GetFullPath(args[1]), schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Schema gerado de PluginManifest: {Path.GetFullPath(args[1])}");
    return 0;
}

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Uso: dotnet run --project PluginDevKit.Tool -- <manifest.json> [diretório do plugin]");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var pluginDirectory = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.GetDirectoryName(manifestPath)!;
var issues = new List<PluginDiagnostic>();
if (!File.Exists(manifestPath)) issues.Add(new("CDK019", $"Manifesto não encontrado: {manifestPath}"));
else
{
    var json = File.ReadAllText(manifestPath);
    issues.AddRange(PluginManifestValidator.ValidateJson(json));
    try
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("manifestVersion", out var version)
            && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number >= 2
            && !Directory.Exists(Path.Combine(pluginDirectory, "Core")))
            issues.Add(new("CDK029", "O projeto de origem v2/v3 requer uma pasta Core portátil. Mova regras e estado para Core."));
    }
    catch (JsonException) { /* CDK001 already reports malformed JSON. */ }
}

var core = Path.Combine(pluginDirectory, "Core");
if (Directory.Exists(core))
{
    var projects = Directory.GetFiles(core, "*.csproj");
    if (projects.Length != 1) issues.Add(new("CDK020", "Core deve conter exatamente um projeto .csproj portátil."));
    foreach (var project in projects)
    {
        var xml = XDocument.Load(project);
        var framework = xml.Descendants("TargetFramework").SingleOrDefault()?.Value;
        if (framework != "net8.0") issues.Add(new("CDK021", $"{Path.GetFileName(project)} deve usar net8.0, sem sufixo de plataforma."));
        if (xml.Descendants().Any(x => x.Name.LocalName is "UseWPF" or "UseWindowsForms" && x.Value.Equals("true", StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("CDK022", "Core não pode habilitar WPF ou Windows Forms. Mova a UI para um renderer."));
        foreach (var reference in xml.Descendants().Where(x => x.Name.LocalName is "ProjectReference" or "PackageReference" or "Reference" or "FrameworkReference"))
        {
            var name = reference.Attribute("Include")?.Value ?? "";
            if (Regex.IsMatch(name, @"(?i)(PluginSdk\.Windows|Microsoft\.Windows|System\.Speech|Win32|\.Wpf|\.Maui)"))
                issues.Add(new("CDK023", $"Core referencia '{name}'. Mova a dependência para um adapter de plataforma."));
        }
    }
    foreach (var file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                 .Where(x => !x.Split(Path.DirectorySeparatorChar).Any(y => y is "obj" or "bin")))
    {
        var source = File.ReadAllText(file);
        var forbidden = new Dictionary<string, string>
        {
            [@"\busing\s+(?:static\s+)?System\.Windows\b"] = "WPF",
            [@"\bSystem\.Windows\."] = "WPF",
            [@"\busing\s+(?:static\s+)?Microsoft\.Win32\b"] = "Win32",
            [@"\busing\s+(?:static\s+)?System\.Speech\b"] = "System.Speech",
            [@"\b(?:DllImport|LibraryImport)\s*\("] = "P/Invoke",
            [@"\b(?:File|Directory)\s*\.\s*(?:Read|Write|Open|Create|Delete|Move|Copy|Enumerate|GetFiles|GetDirectories|Exists)\w*\s*\("] = "acesso direto a arquivos",
            [@"\bSystem\.IO\.(?:File|Directory)\."] = "acesso direto a arquivos",
            [@"\bnew\s+HttpClient\s*\("] = "HTTP direto"
        };
        foreach (var (pattern, label) in forbidden)
            if (Regex.IsMatch(source, pattern))
                issues.Add(new("CDK024", $"{Path.GetRelativePath(pluginDirectory, file)} usa {label}. Solicite uma capability ao host ou mova o código para um adapter."));
    }
}

foreach (var issue in issues) Console.Error.WriteLine($"{issue.Code}: {issue.Message}");
if (issues.Count > 0) return 1;
Console.WriteLine($"Manifesto e arquitetura válidos: {manifestPath}");
return 0;

static JsonObject ManifestSchema(Type type)
{
    if (type == typeof(string)) return new JsonObject { ["type"] = "string" };
    if (type == typeof(int)) return new JsonObject { ["type"] = "integer" };
    if (type == typeof(double)) return new JsonObject { ["type"] = "number" };
    if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        return new JsonObject { ["type"] = "object", ["additionalProperties"] = ManifestSchema(type.GenericTypeArguments[1]) };
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        return new JsonObject { ["type"] = "array", ["items"] = ManifestSchema(type.GenericTypeArguments[0]) };
    var properties = new JsonObject();
    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                 .Where(x => x.CanWrite))
    {
        var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        properties[name] = ManifestSchema(property.PropertyType);
    }
    return new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties };
}
