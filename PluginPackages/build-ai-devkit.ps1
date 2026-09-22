param(
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\ClipDesk-Plugin-AI-DevKit.zip')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('clipdesk-plugin-devkit-' + [Guid]::NewGuid().ToString('N'))
$kitRoot = Join-Path $stagingRoot 'ClipDesk-Plugin-AI-DevKit'

try {
    New-Item -ItemType Directory -Path $kitRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\PLUGIN-AI-DEVKIT.md') -Destination (Join-Path $kitRoot 'README-AI.md')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PluginSdk') -Destination $kitRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PluginSdk.Windows') -Destination $kitRoot -Recurse

    $modules = Join-Path $kitRoot 'PluginPackages\Modules'
    New-Item -ItemType Directory -Path $modules -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Templates\Starter') -Destination $modules -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'pack-plugin.ps1') -Destination (Join-Path $kitRoot 'PluginPackages')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'create-feed-draft.ps1') -Destination (Join-Path $kitRoot 'PluginPackages')

    Get-ChildItem -LiteralPath $kitRoot -Directory -Recurse |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force

    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path $resolvedOutput -Parent
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($kitRoot, $resolvedOutput)
    Get-Item -LiteralPath $resolvedOutput | Select-Object FullName, Length, LastWriteTime
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
