using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Starter;

public sealed class StarterModule : IClipDeskPluginModule
{
    private const int MaximumTextLength = 4_000;

    public string Id => "clipdesk.starter";
    public int StateVersion => 1;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["text"] = "Meu primeiro plugin"
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";
        values["text"] = (state.GetString("text") ?? "").Trim();
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
            "set-text" => ValueTask.FromResult(SetText(state, command.Argument("value"))),
            "clear" => ValueTask.FromResult(new PluginCommandResult(state.With("text", ""))),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("text");

    private static PluginCommandResult SetText(PluginState state, string? value)
    {
        value = value?.Trim() ?? "";
        if (value.Length > MaximumTextLength)
            return PluginCommandResult.Invalid(state, $"Use no máximo {MaximumTextLength} caracteres.");

        return new PluginCommandResult(state.With("text", value));
    }
}
