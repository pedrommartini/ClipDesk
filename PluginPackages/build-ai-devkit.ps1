param(
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\ClipDesk-Plugin-AI-DevKit.zip')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('clipdesk-plugin-devkit-' + [Guid]::NewGuid().ToString('N'))
$kitRoot = Join-Path $stagingRoot 'ClipDesk-Plugin-AI-DevKit'

try {
    New-Item -ItemType Directory -Path $kitRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-DEVKIT.md') -Destination (Join-Path $kitRoot 'README.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PluginSdk') -Destination $kitRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PluginSdk.Windows') -Destination $kitRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PluginDevKit.Tool') -Destination $kitRoot -Recurse
    $kitDocs = $kitRoot
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-ARCHITECTURE.md') -Destination $kitDocs
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-DEVKIT-PLAN.md') -Destination $kitDocs
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-MIGRATION.md') -Destination $kitDocs
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-AI-DEVKIT.md') -Destination $kitDocs
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\DEVKIT-VERSION.md') -Destination $kitDocs

    $modules = Join-Path $kitRoot 'PluginPackages\Modules'
    New-Item -ItemType Directory -Path $modules -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Templates\Starter') -Destination $modules -Recurse
    $examples = Join-Path $kitRoot 'PluginPackages\Examples'
    New-Item -ItemType Directory -Path $examples -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Examples\AudioNotes') -Destination $examples -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Examples\ImageCompressor') -Destination $examples -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'pack-plugin.ps1') -Destination (Join-Path $kitRoot 'PluginPackages')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'pack-reviewed-plugin.ps1') -Destination (Join-Path $kitRoot 'PluginPackages')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'create-feed-draft.ps1') -Destination (Join-Path $kitRoot 'PluginPackages')
    dotnet run --project (Join-Path $repositoryRoot 'PluginDevKit.Tool\ClipDesk.PluginDevKit.Tool.csproj') -- --schema (Join-Path $kitRoot 'manifest-v3.schema.json')
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível gerar o schema do manifesto.' }

    Get-ChildItem -LiteralPath $kitRoot -Directory -Recurse |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Sort-Object FullName -Descending |
        ForEach-Object {
            if (-not [IO.Path]::GetFullPath($_.FullName).StartsWith([IO.Path]::GetFullPath($kitRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Diretório fora do kit: $($_.FullName)"
            }
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }

    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path $resolvedOutput -Parent
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($kitRoot, $resolvedOutput)
    $landingDownloadDirectory = Join-Path $repositoryRoot 'landing\downloads'
    New-Item -ItemType Directory -Path $landingDownloadDirectory -Force | Out-Null
    $landingDownload = Join-Path $landingDownloadDirectory 'ClipDesk-Plugin-AI-DevKit.zip'
    if (-not [string]::Equals($resolvedOutput, [IO.Path]::GetFullPath($landingDownload), [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $resolvedOutput -Destination $landingDownload -Force
    }
    Get-Item -LiteralPath $resolvedOutput | Select-Object FullName, Length, LastWriteTime
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not [IO.Path]::GetFullPath($stagingRoot).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Diretório temporário fora do destino esperado: $stagingRoot"
        }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
