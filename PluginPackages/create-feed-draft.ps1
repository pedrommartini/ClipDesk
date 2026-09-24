param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist/feed.draft.json')
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$entries = @()
foreach ($zip in Get-ChildItem -LiteralPath $packageRoot -Filter 'clipdesk.*.zip' -File) {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
    try {
        $manifestFile = $archive.GetEntry('manifest.json')
        if ($null -eq $manifestFile) { throw "Manifesto ausente: $($zip.Name)" }
        $reader = [IO.StreamReader]::new($manifestFile.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        $windowsRenderer = @($manifest.renderers | Where-Object { $_.platform -eq 'windows' -and $_.runtime -eq 'wpf-v2' }) | Select-Object -First 1
        $isV1 = $manifest.manifestVersion -eq 1 -and $manifest.runtime -eq 'wpf-v1' -and $manifest.pluginApiVersion -eq 1 -and $null -ne $archive.GetEntry($manifest.entryAssembly)
        $isV2 = $manifest.manifestVersion -eq 2 -and $manifest.runtime -eq 'portable-v2' -and $manifest.pluginApiVersion -eq 2 -and
            $null -ne $manifest.module -and $null -ne $windowsRenderer -and
            $null -ne $archive.GetEntry($manifest.module.assembly) -and $null -ne $archive.GetEntry($windowsRenderer.assembly)
        if (-not $isV1 -and -not $isV2) {
            throw "Pacote Windows inválido: $($zip.Name)"
        }
        if ($isV2 -and ($null -eq $manifest.defaultSize -or $null -eq $manifest.minimumSize -or [double]$manifest.minimumSize.width -lt 120 -or
            [Math]::Abs([double]$manifest.minimumSize.width - [double]$manifest.minimumSize.height) -gt 0.01 -or
            [double]$manifest.minimumSize.width -gt [double]$manifest.defaultSize.width -or
            [double]$manifest.minimumSize.height -gt [double]$manifest.defaultSize.height)) {
            throw "minimumSize precisa ser quadrado: $($zip.Name)"
        }
        if ($zip.Name -ne "$($manifest.id)-$($manifest.version).zip") {
            throw "Nome do ZIP não corresponde ao manifesto: $($zip.Name)"
        }
        $entries += [pscustomobject]@{
            manifestVersion = $manifest.manifestVersion
            id = $manifest.id
            name = $manifest.name
            description = $manifest.description
            publisher = $manifest.publisher
            version = $manifest.version
            url = "https://github.com/pedrommartini/ClipDesk/releases/download/plugins-$($manifest.version)/$($zip.Name)"
            sha256 = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            runtime = $manifest.runtime
            pluginApiVersion = $manifest.pluginApiVersion
            minimumHostVersion = $manifest.minimumHostVersion
            maximumHostVersion = $manifest.maximumHostVersion
            entryAssembly = $manifest.entryAssembly
            entryType = $manifest.entryType
            iconGlyph = $manifest.iconGlyph
            accentColor = $manifest.accentColor
            sortOrder = $manifest.sortOrder
            defaultSize = $manifest.defaultSize
            minimumSize = $manifest.minimumSize
            defaultContent = $manifest.defaultContent
            platforms = $manifest.platforms
            stateVersion = $manifest.stateVersion
            module = $manifest.module
            renderers = $manifest.renderers
            permissions = $manifest.permissions
            capabilities = $manifest.capabilities
            networkHosts = $manifest.networkHosts
        }
    }
    finally { $archive.Dispose() }
}
if ($entries.Count -eq 0) { throw 'Nenhum ZIP de plugin encontrado.' }
$feed = [pscustomobject]@{ schemaVersion = 1; packages = @($entries | Sort-Object sortOrder, id) }
$output = [IO.Path]::GetFullPath($OutputPath)
$feed | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $output -Encoding utf8
Write-Output $output
