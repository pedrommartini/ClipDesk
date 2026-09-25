using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClipDesk.PluginSdk;

public sealed record PluginDiagnostic(string Code, string Message);

/// <summary>Shared executable rules for package tools and future hosts.</summary>
public static class PluginManifestValidator
{
    private static readonly Regex Identifier = new("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<PluginDiagnostic> ValidateJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var isV3 = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("manifestVersion", out var version)
                && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 3;
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, isV3 ? StrictJson : LegacyJson);
            return manifest is null
                ? [new("CDK001", "Manifesto vazio. Forneça um objeto JSON.")]
                : Validate(manifest);
        }
        catch (JsonException ex)
        {
            return [new("CDK001", $"Manifesto JSON inválido ou campo desconhecido: {ex.Message} Corrija o nome ou remova o campo.")];
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException or InvalidOperationException)
        {
            return [new("CDK001", $"Manifesto inválido: {ex.Message} Corrija campos nulos ou de formato incorreto.")];
        }
    }

    public static IReadOnlyList<PluginDiagnostic> Validate(PluginManifest manifest)
    {
        try { return ValidateCore(manifest); }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException or InvalidOperationException)
        {
            return [new("CDK001", $"Manifesto inválido: {ex.Message} Corrija campos nulos ou de formato incorreto.")];
        }
    }

    private static IReadOnlyList<PluginDiagnostic> ValidateCore(PluginManifest manifest)
    {
        var issues = new List<PluginDiagnostic>();
        if (!Identifier.IsMatch(manifest.Id ?? "")) issues.Add(new("CDK002", "id deve usar letras minúsculas, dígitos, pontos ou hífens, sem separadores repetidos."));
        if (string.IsNullOrWhiteSpace(manifest.Name)) issues.Add(new("CDK003", "name é obrigatório."));
        if (manifest.ManifestVersion == 3
            ? !PluginSemanticVersion.TryParse(manifest.Version, out _)
            : !Version.TryParse(manifest.Version, out _))
            issues.Add(new("CDK004", "version deve ser MAJOR.MINOR.PATCH na v3 ou uma versão numérica nas versões legadas."));
        if (manifest.ManifestVersion is < 1 or > 3) issues.Add(new("CDK031", "manifestVersion deve ser 1, 2 ou 3."));
        if (manifest.ManifestVersion != 3) return issues;

        if (manifest.Runtime != PluginRuntimes.PortableV3 || manifest.PluginApiVersion != 3
            || !PluginSemanticVersion.TryParse(manifest.ContractVersion, out var contract) || contract.Major != 3)
            issues.Add(new("CDK005", "Manifesto v3 requer runtime portable-v3, pluginApiVersion 3 e contractVersion 3.x.y."));
        if (manifest.StateVersion < 1) issues.Add(new("CDK006", "stateVersion deve ser positivo."));
        if (manifest.Platforms is null || manifest.Platforms.Count == 0 || manifest.Platforms.Any(x => string.IsNullOrWhiteSpace(x)))
            issues.Add(new("CDK007", "platforms deve declarar ao menos uma plataforma."));
        if (!ValidEntry(manifest.Module)) issues.Add(new("CDK008", "module requer registration estático ou assembly e type; caminhos de assembly não são permitidos."));
        foreach (var platform in manifest.Platforms ?? [])
            if (manifest.RendererFor(platform) is not { } renderer || !ValidEntry(renderer))
                issues.Add(new("CDK009", $"Falta renderer válido para {platform}. Declare registration ou assembly e type."));
        if (manifest.Renderers is not null && manifest.Renderers.Where(x => x is not null)
                .GroupBy(x => x.Platform, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            issues.Add(new("CDK010", "Há renderers duplicados para uma plataforma."));
        if (!ValidLayout(manifest.Layout)) issues.Add(new("CDK011", "layout deve usar dimensões positivas e minimum <= preferred <= maximum quando informados."));
        var permissions = new HashSet<string>(manifest.Permissions ?? [], StringComparer.OrdinalIgnoreCase);
        if ((manifest.NetworkHosts?.Count ?? 0) > 0 && !permissions.Contains(PluginPermissions.Network))
            issues.Add(new("CDK012", "networkHosts exige permission network. Adicione-a ao manifesto."));
        foreach (var host in manifest.NetworkHosts ?? [])
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('/') || host.Contains(':'))
                issues.Add(new("CDK013", $"networkHosts contém '{host}' inválido. Use somente um nome de host."));
        foreach (var requirement in manifest.Requirements ?? [])
        {
            if (string.IsNullOrWhiteSpace(requirement.Id) || !PluginSemanticVersion.TryParse(requirement.MinimumVersion, out _))
                issues.Add(new("CDK014", "Cada requirement exige id e minimumVersion MAJOR.MINOR.PATCH."));
            if (requirement.Permission is { } permission && !permissions.Contains(permission))
                issues.Add(new("CDK015", $"Capability '{requirement.Id}' exige permission '{permission}'. Declare-a em permissions."));
            var expected = PluginHostCapabilityIds.RequiredPermission(requirement.Id);
            if (expected is not null && (!permissions.Contains(expected)
                || !string.Equals(requirement.Permission, expected, StringComparison.OrdinalIgnoreCase)))
                issues.Add(new("CDK025", $"Capability '{requirement.Id}' requer permission '{expected}' no requisito e em permissions."));
        }
        if ((manifest.Requirements ?? []).Any(x => x.Id == PluginHostCapabilityIds.Http)
            && (manifest.NetworkHosts?.Count ?? 0) == 0)
            issues.Add(new("CDK030", "host.http requer networkHosts com pelo menos um domínio autorizado."));
        if ((manifest.Requirements ?? []).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            issues.Add(new("CDK028", "Há requirements duplicados. Declare cada capability apenas uma vez."));
        foreach (var extension in manifest.PlatformExtensions ?? [])
        {
            if (!(manifest.Platforms ?? []).Contains(extension.Platform, StringComparer.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(extension.Capability))
                issues.Add(new("CDK016", "Cada platformExtension requer platform presente em platforms e capability."));
            if (extension.Permission is { } permission && !permissions.Contains(permission))
                issues.Add(new("CDK017", $"Extensão '{extension.Capability}' exige permission '{permission}'."));
            var required = PluginHostCapabilityIds.RequiredPermission(extension.Capability);
            if (required is not null && !string.Equals(extension.Permission, required, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("CDK032", $"Extensão '{extension.Capability}' requer permission '{required}'."));
            var declaredRequirement = (manifest.Requirements ?? []).FirstOrDefault(x =>
                string.Equals(x.Id, extension.Capability, StringComparison.OrdinalIgnoreCase));
            if (declaredRequirement is null || !declaredRequirement.Optional)
                issues.Add(new("CDK033", $"Extensão '{extension.Capability}' deve constar em requirements como opcional."));
            if (string.IsNullOrWhiteSpace(extension.FallbackCapability) && (manifest.Platforms?.Count ?? 0) > 1)
                issues.Add(new("CDK018", $"Extensão '{extension.Capability}' requer fallbackCapability para as demais plataformas."));
            else if (!string.IsNullOrWhiteSpace(extension.FallbackCapability)
                && !(manifest.Requirements ?? []).Any(x => string.Equals(x.Id, extension.FallbackCapability, StringComparison.OrdinalIgnoreCase)))
                issues.Add(new("CDK034", $"fallbackCapability '{extension.FallbackCapability}' deve constar em requirements."));
        }
        return issues;
    }

    private static bool ValidEntry(PluginEntryPoint? entry) => entry is not null
        && (!string.IsNullOrWhiteSpace(entry.Registration)
            || (!string.IsNullOrWhiteSpace(entry.Type) && !string.IsNullOrWhiteSpace(entry.Assembly)
                && Path.GetFileName(entry.Assembly) == entry.Assembly));

    private static bool ValidLayout(PluginLayoutSpec? layout)
    {
        if (layout is null) return true;
        static bool Positive(PluginSize? size) => size is null || double.IsFinite(size.Width) && double.IsFinite(size.Height)
            && size.Width > 0 && size.Height > 0;
        static bool Ordered(PluginSize? smaller, PluginSize? larger) => smaller is null || larger is null
            || smaller.Width <= larger.Width && smaller.Height <= larger.Height;
        return Positive(layout.Minimum) && Positive(layout.Preferred) && Positive(layout.Maximum)
            && Ordered(layout.Minimum, layout.Preferred) && Ordered(layout.Preferred, layout.Maximum)
            && Ordered(layout.Minimum, layout.Maximum)
            && (layout.CompactBelowWidth is null || layout.CompactBelowWidth > 0)
            && (layout.CompactBelowHeight is null || layout.CompactBelowHeight > 0);
    }
}
