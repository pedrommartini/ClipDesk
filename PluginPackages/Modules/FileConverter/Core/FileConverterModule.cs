using System.Globalization;
using ClipDesk.Plugin.FileConverter.Models;
using ClipDesk.Plugin.FileConverter.Services;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.FileConverter;

public sealed class FileConverterModule : IClipDeskPluginModule
{
    public string Id => "clipdesk.fileconverter";
    public int StateVersion => 2;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "2",
        ["sourceAssetId"] = "",
        ["sourceFileName"] = "",
        ["sourceExtension"] = "",
        ["sourceCategory"] = "",
        ["sourceSize"] = "0",
        ["targetFormat"] = "",
        ["quality"] = "85",
        ["icoSize"] = "32",
        ["prettyPrint"] = "true",
        ["status"] = "idle",
        ["outputAssetId"] = "",
        ["outputFileName"] = "",
        ["lastMessage"] = "Aguardando arquivo..."
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "2";

        values.Remove("sourcePath");
        values.Remove("lastConvertedFile");
        values["sourceAssetId"] = AssetId(state.GetString("sourceAssetId"));
        values["sourceFileName"] = state.GetString("sourceFileName") ?? "";
        values["sourceExtension"] = (state.GetString("sourceExtension") ?? "").TrimStart('.').ToLowerInvariant();
        values["sourceCategory"] = state.GetString("sourceCategory") ?? "";
        values["sourceSize"] = state.GetString("sourceSize") ?? "0";
        values["targetFormat"] = (state.GetString("targetFormat") ?? "").TrimStart('.').ToLowerInvariant();

        int quality = state.GetInt32("quality", 85);
        values["quality"] = Math.Clamp(quality, 10, 100).ToString(CultureInfo.InvariantCulture);

        values["icoSize"] = state.GetString("icoSize") ?? "32";
        values["prettyPrint"] = string.Equals(state.GetString("prettyPrint"), "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
        values["status"] = state.GetString("status") ?? "idle";
        values["outputAssetId"] = AssetId(state.GetString("outputAssetId"));
        values["outputFileName"] = Path.GetFileName(state.GetString("outputFileName") ?? "");
        values["lastMessage"] = state.GetString("lastMessage") ?? "";

        // Se temos uma extensão de origem mas nenhum targetFormat válido selecionado, escolhe o primeiro compatível
        if (!string.IsNullOrEmpty(values["sourceExtension"]))
        {
            var compat = FormatRegistry.GetCompatibleTargetFormats(values["sourceExtension"]);
            if (!compat.Any(c => string.Equals(c.Extension, values["targetFormat"], StringComparison.OrdinalIgnoreCase)))
            {
                values["targetFormat"] = compat.FirstOrDefault()?.Extension ?? "";
            }
        }

        return new PluginState(values);
    }

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);

        return command.Name switch
        {
            "set-source" => ValueTask.FromResult(SetSource(state, command)),
            "set-target" => ValueTask.FromResult(SetTarget(state, command)),
            "set-option" => ValueTask.FromResult(SetOption(state, command)),
            "set-status" => ValueTask.FromResult(SetStatus(state, command)),
            "reset" => ValueTask.FromResult(new PluginCommandResult(CreateDefaultState())),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, $"Comando desconhecido: '{command.Name}'."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        var s = NormalizeState(state);
        var lastFile = s.GetString("outputFileName");
        if (!string.IsNullOrWhiteSpace(lastFile))
            return lastFile;

        var source = s.GetString("sourceFileName");
        var target = s.GetString("targetFormat");
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
            return $"{source} -> .{target}";

        return "Conversor de Arquivos ClipDesk";
    }

    private static PluginCommandResult SetSource(PluginState state, PluginCommand command)
    {
        string assetId = AssetId(command.Argument("assetId"));
        string fileName = command.Argument("fileName") ?? "";
        string size = command.Argument("size") ?? "0";

        if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(fileName))
            return PluginCommandResult.Invalid(state, "Anexo compartilhado não fornecido.");

        fileName = Path.GetFileName(fileName);
        string ext = Path.GetExtension(fileName);

        ext = ext.TrimStart('.').ToLowerInvariant();
        var category = FormatRegistry.DetectCategory(ext);

        if (category == FileCategory.Unknown)
        {
            return new PluginCommandResult(
                state.With("status", "error").With("lastMessage", $"Formato '.{ext}' não é suportado para conversão."),
                PluginCommandStatus.ValidationError,
                $"Formato '.{ext}' não é suportado.");
        }

        var targets = FormatRegistry.GetCompatibleTargetFormats(ext);
        string defaultTarget = targets.FirstOrDefault()?.Extension ?? "";

        var next = state
            .With("sourceAssetId", assetId)
            .With("outputAssetId", "")
            .With("outputFileName", "")
            .With("sourceFileName", fileName)
            .With("sourceExtension", ext)
            .With("sourceCategory", category.ToString())
            .With("sourceSize", size)
            .With("targetFormat", defaultTarget)
            .With("status", "idle")
            .With("lastMessage", $"Arquivo carregado ({FormatRegistry.GetCategoryDisplayName(category)}).");

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult SetTarget(PluginState state, PluginCommand command)
    {
        string target = (command.Argument("format") ?? "").TrimStart('.').ToLowerInvariant();
        string sourceExt = state.GetString("sourceExtension") ?? "";

        if (string.IsNullOrWhiteSpace(target))
            return PluginCommandResult.Invalid(state, "Formato de destino não especificado.");

        if (!FormatRegistry.IsConversionAllowed(sourceExt, target, out string? error))
        {
            return PluginCommandResult.Invalid(state, error ?? "Conversão não permitida.");
        }

        var next = state.With("targetFormat", target).With("lastMessage", $"Destino definido para .{target.ToUpperInvariant()}");
        return new PluginCommandResult(next);
    }

    private static PluginCommandResult SetOption(PluginState state, PluginCommand command)
    {
        string key = command.Argument("key") ?? "";
        string value = command.Argument("value") ?? "";

        if (key is not ("quality" or "icoSize" or "prettyPrint"))
            return PluginCommandResult.Invalid(state, "Chave da opção não informada.");

        var next = state.With(key, value);
        return new PluginCommandResult(next);
    }

    private static string AssetId(string? value) => Guid.TryParse(value, out var id) ? id.ToString("N") : "";

    private static PluginCommandResult SetStatus(PluginState state, PluginCommand command)
    {
        string status = command.Argument("status") ?? "idle";
        string message = command.Argument("message") ?? "";
        string outputId = AssetId(command.Argument("assetId"));
        string outputName = Path.GetFileName(command.Argument("fileName") ?? "");
        if (status == "success" && string.IsNullOrWhiteSpace(outputId))
            return PluginCommandResult.Invalid(state, "O resultado precisa de um anexo compartilhado.");

        var next = state
            .With("status", status)
            .With("lastMessage", message)
            .With("outputAssetId", outputId)
            .With("outputFileName", outputName);

        return new PluginCommandResult(next);
    }
}
