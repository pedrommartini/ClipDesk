using System.Globalization;
using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageCompressor;

public static class ImageCompressorCommands
{
    public const string SetSettings = "set-settings";
    public const string ResetSettings = "reset-settings";
    public const string RecordCompression = "record-compression";

    public static PluginCommandResult Failed(PluginState state, string message) =>
        new(state, PluginCommandStatus.Failed, message);

    public static ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = ImageCompressorState.Normalize(state);

        var commandName = command?.Name?.Trim() ?? string.Empty;

        return commandName.ToLowerInvariant() switch
        {
            SetSettings => ValueTask.FromResult(ExecuteSetSettings(state, command!)),
            ResetSettings or "reset" => ValueTask.FromResult(ExecuteResetSettings(state)),
            RecordCompression => ValueTask.FromResult(ExecuteRecordCompression(state, command!)),
            _ => ValueTask.FromResult(Failed(state, $"Comando desconhecido: '{commandName}'"))
        };
    }

    private static PluginCommandResult ExecuteSetSettings(PluginState state, PluginCommand command)
    {
        var qualityArg = command.Argument(ImageCompressorState.Quality);
        var presetArg = command.Argument(ImageCompressorState.Preset);
        var dimArg = command.Argument(ImageCompressorState.MaxDimension) ?? command.Argument("dimension");
        var formatArg = command.Argument(ImageCompressorState.OutputFormat) ?? command.Argument("format");
        var stripArg = command.Argument(ImageCompressorState.StripMetadata);
        var msgArg = command.Argument(ImageCompressorState.LastMessage) ?? command.Argument("message");

        int? newQuality = null;
        if (!string.IsNullOrWhiteSpace(qualityArg))
        {
            if (!int.TryParse(qualityArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuality) ||
                parsedQuality < ImageCompressorState.MinQuality ||
                parsedQuality > ImageCompressorState.MaxQuality)
            {
                return PluginCommandResult.Invalid(state, "A qualidade deve ser um número inteiro entre 10 e 100.");
            }
            newQuality = parsedQuality;
        }

        string? newPreset = null;
        if (!string.IsNullOrWhiteSpace(presetArg))
        {
            var p = presetArg.Trim().ToLowerInvariant();
            if (p is not ("economy" or "balanced" or "fidelity" or "custom"))
            {
                return PluginCommandResult.Invalid(state, $"Preset inválido: '{presetArg}'.");
            }
            newPreset = p;
        }

        string? newDim = null;
        if (!string.IsNullOrWhiteSpace(dimArg))
        {
            var d = dimArg.Trim().ToLowerInvariant();
            if (d is "original" or "0")
            {
                newDim = "original";
            }
            else if (d is "1920" or "fullhd" or "fhd")
            {
                newDim = "1920";
            }
            else if (d is "1280" or "hd" or "720p")
            {
                newDim = "1280";
            }
            else
            {
                return PluginCommandResult.Invalid(state, $"Dimensão máxima inválida: '{dimArg}'.");
            }
        }

        string? newFormat = null;
        if (!string.IsNullOrWhiteSpace(formatArg))
        {
            var f = formatArg.Trim().ToLowerInvariant();
            if (f is "original")
            {
                newFormat = "original";
            }
            else if (f is "jpeg" or "jpg")
            {
                newFormat = "jpeg";
            }
            else if (f is "png")
            {
                newFormat = "png";
            }
            else
            {
                return PluginCommandResult.Invalid(state, $"Formato de saída inválido: '{formatArg}'.");
            }
        }

        string? newStrip = null;
        if (!string.IsNullOrWhiteSpace(stripArg))
        {
            var s = stripArg.Trim().ToLowerInvariant();
            if (s is not ("true" or "false"))
            {
                return PluginCommandResult.Invalid(state, "O parâmetro stripMetadata deve ser 'true' ou 'false'.");
            }
            newStrip = s;
        }

        // Coerência entre qualidade e preset
        if (newPreset is not null && newQuality is null)
        {
            newQuality = newPreset switch
            {
                "economy" => 55,
                "balanced" => 75,
                "fidelity" => 90,
                _ => ImageCompressorState.GetQuality(state)
            };
        }
        else if (newQuality is not null && newPreset is null)
        {
            newPreset = newQuality switch
            {
                55 => "economy",
                75 => "balanced",
                90 => "fidelity",
                _ => "custom"
            };
        }

        var nextState = state;
        if (newQuality.HasValue)
        {
            nextState = nextState.With(ImageCompressorState.Quality, newQuality.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (newPreset is not null)
        {
            nextState = nextState.With(ImageCompressorState.Preset, newPreset);
        }
        if (newDim is not null)
        {
            nextState = nextState.With(ImageCompressorState.MaxDimension, newDim);
        }
        if (newFormat is not null)
        {
            nextState = nextState.With(ImageCompressorState.OutputFormat, newFormat);
        }
        if (newStrip is not null)
        {
            nextState = nextState.With(ImageCompressorState.StripMetadata, newStrip);
        }
        if (!string.IsNullOrWhiteSpace(msgArg))
        {
            nextState = nextState.With(ImageCompressorState.LastMessage, msgArg.Trim());
        }

        return new PluginCommandResult(nextState, PluginCommandStatus.Success, "Configurações atualizadas.");
    }

    private static PluginCommandResult ExecuteResetSettings(PluginState state)
    {
        // Preserva as métricas acumuladas de vida útil (lifetime)
        var totalFiles = state.GetString(ImageCompressorState.TotalFilesCompressed) ?? ImageCompressorState.DefaultTotalFilesCompressed;
        var totalBytes = state.GetString(ImageCompressorState.TotalBytesSaved) ?? ImageCompressorState.DefaultTotalBytesSaved;

        var next = ImageCompressorState.CreateDefault()
            .With(ImageCompressorState.TotalFilesCompressed, totalFiles)
            .With(ImageCompressorState.TotalBytesSaved, totalBytes)
            .With(ImageCompressorState.LastMessage, "Configurações restauradas para o padrão.");

        next = CompressorSharedState.WithItems(next, CompressorSharedState.ReadSingle(state), CompressorSharedState.ReadBatch(state), state.GetString(CompressorSharedState.BatchMode) == "true");
        return new PluginCommandResult(next, PluginCommandStatus.Success, "Configurações restauradas para o padrão.");
    }

    private static PluginCommandResult ExecuteRecordCompression(PluginState state, PluginCommand command)
    {
        var filesArg = command.Argument("filesCount") ?? command.Argument("count") ?? command.Argument("files");
        var bytesArg = command.Argument("bytesSaved") ?? command.Argument("bytes");

        if (string.IsNullOrWhiteSpace(filesArg) ||
            !int.TryParse(filesArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var filesCount) ||
            filesCount < 0)
        {
            return PluginCommandResult.Invalid(state, "A quantidade de arquivos deve ser um número inteiro não negativo.");
        }

        if (string.IsNullOrWhiteSpace(bytesArg) ||
            !long.TryParse(bytesArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytesSaved) ||
            bytesSaved < 0)
        {
            return PluginCommandResult.Invalid(state, "O total de bytes economizados deve ser um número não negativo.");
        }

        var currentFiles = ImageCompressorState.GetTotalFilesCompressed(state);
        var currentBytes = ImageCompressorState.GetTotalBytesSaved(state);

        long newTotalFiles = currentFiles + filesCount;
        long newTotalBytes = currentBytes + bytesSaved;

        var message = $"Comprimido(s) {filesCount} arquivo(s) com {FormatBytes(bytesSaved)} economizados.";

        var next = state
            .With(ImageCompressorState.TotalFilesCompressed, newTotalFiles.ToString(CultureInfo.InvariantCulture))
            .With(ImageCompressorState.TotalBytesSaved, newTotalBytes.ToString(CultureInfo.InvariantCulture))
            .With(ImageCompressorState.LastMessage, message);

        return new PluginCommandResult(next, PluginCommandStatus.Success, "Compressão registrada com sucesso.");
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unitIndex = 0;
        double size = bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size:0.##} {units[unitIndex]}";
    }
}
