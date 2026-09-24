using ClipDesk.PluginSdk;

namespace ClipDesk.Core;

/// <summary>
/// Compatibility bridge while the original four WPF widgets move to external
/// packages. New board data uses stable plugin IDs instead of enum values.
/// </summary>
public static class BoardPluginIdentity
{
    public const string InitialVersion = "1.0.0";

    public static string? FromKind(BoardObjectKind kind) => kind switch
    {
        BoardObjectKind.Checklist => BuiltInPluginIds.Checklist,
        BoardObjectKind.Calculator => BuiltInPluginIds.Calculator,
        BoardObjectKind.Translator => BuiltInPluginIds.Translator,
        BoardObjectKind.CurrencyConverter => BuiltInPluginIds.CurrencyConverter,
        _ => null
    };

    public static string? NameFromKind(BoardObjectKind kind) => kind switch
    {
        BoardObjectKind.Checklist => "Checklist",
        BoardObjectKind.Calculator => "Calculadora",
        BoardObjectKind.Translator => "Tradutor",
        BoardObjectKind.CurrencyConverter => "Conversor de moeda",
        _ => null
    };

    public static bool Normalize(BoardObject obj)
    {
        var id = FromKind(obj.Kind);
        if (id is null) return false;
        var changed = false;
        if (!string.Equals(obj.PluginId, id, StringComparison.Ordinal))
        {
            obj.PluginId = id;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(obj.PluginVersion))
        {
            obj.PluginVersion = InitialVersion;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(obj.PluginName))
        {
            obj.PluginName = NameFromKind(obj.Kind);
            changed = true;
        }
        return changed;
    }
}
