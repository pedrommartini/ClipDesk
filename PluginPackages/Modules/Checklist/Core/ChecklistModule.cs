using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Checklist;

public sealed record ChecklistItem(string Id, string Text, bool IsCompleted);

public sealed class ChecklistModule : IClipDeskPluginModule
{
    public string Id => BuiltInPluginIds.Checklist;
    public int StateVersion => 2;

    public PluginState CreateDefaultState() => Write(new PluginState(), "Checklist",
    [
        new(Guid.NewGuid().ToString("N"), "Primeiro item", false),
        new(Guid.NewGuid().ToString("N"), "Segundo item", false),
        new(Guid.NewGuid().ToString("N"), "Terceiro item", false)
    ]);

    public PluginState NormalizeState(PluginState state)
    {
        var title = state.GetString("title") ?? "Checklist";
        var items = ReadItems(state);
        return Write(state, title, items);
    }

    public ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);
        var title = state.GetString("title") ?? "Checklist";
        var items = ReadItems(state).ToList();
        switch (command.Name)
        {
            case "set-title":
                title = Sanitize(command.Argument("title"), 120, "Checklist");
                break;
            case "add":
                items.Add(new ChecklistItem(Guid.NewGuid().ToString("N"), Sanitize(command.Argument("text"), 500, "Novo item"), false));
                break;
            case "update":
                if (!Find(items, command.Argument("id"), out var updateIndex)) return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Item não encontrado."));
                items[updateIndex] = items[updateIndex] with { Text = Sanitize(command.Argument("text"), 500, "Novo item") };
                break;
            case "toggle":
                if (!Find(items, command.Argument("id"), out var toggleIndex)) return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Item não encontrado."));
                items[toggleIndex] = items[toggleIndex] with { IsCompleted = !items[toggleIndex].IsCompleted };
                break;
            case "remove":
                if (!Find(items, command.Argument("id"), out var removeIndex)) return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Item não encontrado."));
                items.RemoveAt(removeIndex);
                break;
            default:
                return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."));
        }
        return ValueTask.FromResult(new PluginCommandResult(Write(state, title, items)));
    }

    public string? GetClipboardText(PluginState state)
    {
        var items = ReadItems(NormalizeState(state));
        return string.Join(Environment.NewLine, items.Select(item => $"{(item.IsCompleted ? "[x]" : "[ ]")} {item.Text}"));
    }

    public static IReadOnlyList<ChecklistItem> ReadItems(PluginState state)
    {
        var json = state.GetString("itemsJson");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { return JsonSerializer.Deserialize<List<ChecklistItem>>(json) ?? []; }
            catch (JsonException) { }
        }
        var legacyItems = (state.GetString("items") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var completed = (state.GetString("checked") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : -1)
            .Where(index => index >= 0).ToHashSet();
        return legacyItems.Select((text, index) => new ChecklistItem(LegacyId(index, text), text.TrimEnd('\r'), completed.Contains(index))).ToList();
    }

    private PluginState Write(PluginState state, string title, IReadOnlyList<ChecklistItem> items)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = StateVersion.ToString(CultureInfo.InvariantCulture);
        values["title"] = title;
        values["itemsJson"] = JsonSerializer.Serialize(items);
        values["items"] = string.Join('\n', items.Select(item => item.Text.Replace('\n', ' ')));
        values["checked"] = string.Join(',', items.Select((item, index) => (item, index)).Where(x => x.item.IsCompleted).Select(x => x.index));
        return new PluginState(values);
    }

    private static bool Find(IReadOnlyList<ChecklistItem> items, string? id, out int index)
    { index = items.ToList().FindIndex(item => item.Id == id); return index >= 0; }
    private static string Sanitize(string? value, int maxLength, string fallback)
    { var clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim(); return clean.Length == 0 ? fallback : clean[..Math.Min(clean.Length, maxLength)]; }
    private static string LegacyId(int index, string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{index}:{text}")))[..16].ToLowerInvariant();
}
