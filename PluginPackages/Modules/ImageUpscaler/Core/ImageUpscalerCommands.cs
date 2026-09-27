using System.Globalization;
using ClipDesk.Plugin.ImageUpscaler.Models;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageUpscaler;

public static class ImageUpscalerCommands
{
    public const string SetSettings = "set-settings";
    public const string ResetSettings = "reset-settings";
    public const string SetInput = "set-input";
    public const string RecordUpscale = "record-upscale";

    public static PluginCommandResult Failed(PluginState state, string message) =>
        new(state, PluginCommandStatus.Failed, message);

    public static ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = ImageUpscalerState.Normalize(state);

        var commandName = command?.Name?.Trim() ?? string.Empty;

        return commandName.ToLowerInvariant() switch
        {
            SetSettings => ValueTask.FromResult(ExecuteSetSettings(state, command!)),
            ResetSettings or "reset" => ValueTask.FromResult(ExecuteResetSettings(state)),
            SetInput => ValueTask.FromResult(ExecuteSetInput(state, command!)),
            RecordUpscale => ValueTask.FromResult(ExecuteRecordUpscale(state, command!)),
            _ => ValueTask.FromResult(Failed(state, $"Comando desconhecido: '{commandName}'"))
        };
    }

    private static PluginCommandResult ExecuteSetSettings(PluginState state, PluginCommand command)
    {
        var factorArg = command.Argument(ImageUpscalerState.ScaleFactor) ?? command.Argument("factor") ?? command.Argument("scale");
        var modelArg = command.Argument(ImageUpscalerState.Model);
        var engineArg = command.Argument(ImageUpscalerState.Engine);
        var sharpnessArg = command.Argument(ImageUpscalerState.Sharpness);
        var denoiseArg = command.Argument(ImageUpscalerState.Denoise);
        var formatArg = command.Argument(ImageUpscalerState.OutputFormat) ?? command.Argument("format");
        var faceArg = command.Argument(ImageUpscalerState.FaceEnhance);
        var cloudArg = command.Argument(ImageUpscalerState.CloudEndpoint);
        var msgArg = command.Argument(ImageUpscalerState.LastMessage) ?? command.Argument("message");

        string? newFactor = null;
        if (!string.IsNullOrWhiteSpace(factorArg))
        {
            var f = factorArg.Trim().ToLowerInvariant();
            if (f is not ("2x" or "2" or "4x" or "4" or "8x" or "8"))
            {
                return PluginCommandResult.Invalid(state, $"Fator de escala inválido: '{factorArg}'. Use 2x, 4x ou 8x.");
            }
            newFactor = EnumExtensions.ParseUpscaleFactor(f).ToWireString();
        }

        string? newModel = null;
        if (!string.IsNullOrWhiteSpace(modelArg))
        {
            var m = modelArg.Trim().ToLowerInvariant();
            if (m is not ("balanced" or "photo" or "portrait" or "illustration" or "anime" or "art" or "crisp-text" or "text" or "document"))
            {
                return PluginCommandResult.Invalid(state, $"Modelo de upscale inválido: '{modelArg}'.");
            }
            newModel = EnumExtensions.ParseUpscaleModel(m).ToWireString();
        }

        string? newEngine = null;
        if (!string.IsNullOrWhiteSpace(engineArg))
        {
            var e = engineArg.Trim().ToLowerInvariant();
            if (e is not ("local" or "cloud" or "ai"))
            {
                return PluginCommandResult.Invalid(state, $"Engine inválida: '{engineArg}'. Use 'local' ou 'cloud'.");
            }
            newEngine = EnumExtensions.ParseUpscaleEngine(e).ToWireString();
        }

        int? newSharpness = null;
        if (!string.IsNullOrWhiteSpace(sharpnessArg))
        {
            if (!int.TryParse(sharpnessArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ||
                s < ImageUpscalerState.MinSharpness || s > ImageUpscalerState.MaxSharpness)
            {
                return PluginCommandResult.Invalid(state, "A nitidez deve ser um número inteiro entre 0 e 100.");
            }
            newSharpness = s;
        }

        string? newDenoise = null;
        if (!string.IsNullOrWhiteSpace(denoiseArg))
        {
            newDenoise = EnumExtensions.ParseDenoiseLevel(denoiseArg).ToWireString();
        }

        string? newFormat = null;
        if (!string.IsNullOrWhiteSpace(formatArg))
        {
            newFormat = EnumExtensions.ParseOutputFormat(formatArg).ToWireString();
        }

        string? newFace = null;
        if (!string.IsNullOrWhiteSpace(faceArg))
        {
            newFace = string.Equals(faceArg, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        }

        var dict = state.ToDictionary();
        if (newFactor is not null) dict[ImageUpscalerState.ScaleFactor] = newFactor;
        if (newModel is not null) dict[ImageUpscalerState.Model] = newModel;
        if (newEngine is not null) dict[ImageUpscalerState.Engine] = newEngine;
        if (newSharpness.HasValue) dict[ImageUpscalerState.Sharpness] = newSharpness.Value.ToString(CultureInfo.InvariantCulture);
        if (newDenoise is not null) dict[ImageUpscalerState.Denoise] = newDenoise;
        if (newFormat is not null) dict[ImageUpscalerState.OutputFormat] = newFormat;
        if (newFace is not null) dict[ImageUpscalerState.FaceEnhance] = newFace;
        if (cloudArg is not null) dict[ImageUpscalerState.CloudEndpoint] = cloudArg;
        if (!string.IsNullOrWhiteSpace(msgArg)) dict[ImageUpscalerState.LastMessage] = msgArg.Trim();

        return new PluginCommandResult(new PluginState(dict));
    }

    private static PluginCommandResult ExecuteResetSettings(PluginState state)
    {
        var totalCount = state.GetString(ImageUpscalerState.TotalImagesUpscaled) ?? ImageUpscalerState.DefaultTotalImagesUpscaled;
        var totalPixels = state.GetString(ImageUpscalerState.TotalPixelsGenerated) ?? ImageUpscalerState.DefaultTotalPixelsGenerated;

        var defaultDict = ImageUpscalerState.CreateDefault().ToDictionary();
        foreach (var key in new[] { UpscaleSharedState.InputAsset, UpscaleSharedState.OutputAsset, UpscaleSharedState.OutputInfo })
            defaultDict[key] = state.GetString(key) ?? "";
        defaultDict[ImageUpscalerState.TotalImagesUpscaled] = totalCount;
        defaultDict[ImageUpscalerState.TotalPixelsGenerated] = totalPixels;
        defaultDict[ImageUpscalerState.LastMessage] = "Configurações restauradas";

        return new PluginCommandResult(new PluginState(defaultDict));
    }

    private static PluginCommandResult ExecuteSetInput(PluginState state, PluginCommand command)
    {
        var id = UpscaleSharedState.AssetId(command.Argument(UpscaleSharedState.InputAsset));
        if (string.IsNullOrEmpty(id)) return PluginCommandResult.Invalid(state, "Selecione um anexo compartilhado válido.");
        return new PluginCommandResult(state.With(UpscaleSharedState.InputAsset, id)
            .With(UpscaleSharedState.OutputAsset, "").With(UpscaleSharedState.OutputInfo, ""));
    }

    private static PluginCommandResult ExecuteRecordUpscale(PluginState state, PluginCommand command)
    {
        var inputId = UpscaleSharedState.AssetId(command.Argument(UpscaleSharedState.InputAsset));
        var outputId = UpscaleSharedState.AssetId(command.Argument(UpscaleSharedState.OutputAsset));
        var outputState = state.With(UpscaleSharedState.OutputInfo, command.Argument(UpscaleSharedState.OutputInfo) ?? "");
        var info = UpscaleSharedState.ReadOutput(outputState);
        if (inputId != state.GetString(UpscaleSharedState.InputAsset) || string.IsNullOrEmpty(outputId) || info is null)
            return PluginCommandResult.Invalid(state, "A entrada mudou ou o resultado compartilhado não é válido.");
        var pixelsArg = command.Argument("pixels") ?? command.Argument("pixelsGenerated");
        long newPixels = 0;
        if (!string.IsNullOrWhiteSpace(pixelsArg) &&
            long.TryParse(pixelsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0)
        {
            newPixels = p;
        }

        var currentCount = ImageUpscalerState.GetTotalImagesUpscaled(state);
        var currentPixels = ImageUpscalerState.GetTotalPixelsGenerated(state);

        var updatedCount = currentCount + 1;
        var updatedPixels = currentPixels + newPixels;

        var msg = command.Argument(ImageUpscalerState.LastMessage) ?? command.Argument("message") ?? "Imagem ampliada com sucesso!";

        var dict = state.ToDictionary();
        dict[UpscaleSharedState.OutputAsset] = outputId;
        dict[UpscaleSharedState.OutputInfo] = System.Text.Json.JsonSerializer.Serialize(info);
        dict[ImageUpscalerState.TotalImagesUpscaled] = updatedCount.ToString(CultureInfo.InvariantCulture);
        dict[ImageUpscalerState.TotalPixelsGenerated] = updatedPixels.ToString(CultureInfo.InvariantCulture);
        dict[ImageUpscalerState.LastMessage] = msg;

        return new PluginCommandResult(new PluginState(dict));
    }
}
