param(
    [Parameter(Mandatory = $true)][string]$PluginDirectory,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$moduleDirectory = (Resolve-Path -LiteralPath $PluginDirectory).Path
$manifestPath = Join-Path $moduleDirectory 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$projects = @(Get-ChildItem -LiteralPath $moduleDirectory -Filter '*.csproj' -File)
if ($projects.Count -ne 1) { throw 'O diretório do plugin deve conter exatamente um projeto .csproj.' }
$isV1 = $manifest.manifestVersion -eq 1 -and $manifest.runtime -eq 'wpf-v1' -and $manifest.pluginApiVersion -eq 1
$windowsRenderer = @($manifest.renderers | Where-Object { $_.platform -eq 'windows' -and $_.runtime -eq 'wpf-v2' }) | Select-Object -First 1
$isV2 = $manifest.manifestVersion -eq 2 -and $manifest.runtime -eq 'portable-v2' -and
    $manifest.pluginApiVersion -eq 2 -and $null -ne $manifest.module -and $null -ne $windowsRenderer
if (-not $isV1 -and -not $isV2) { throw 'O manifesto precisa declarar wpf-v1/API 1 ou portable-v2/API 2 com renderizador wpf-v2.' }

$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$staging = Join-Path $output ('.staging-' + [Guid]::NewGuid().ToString('N'))
$published = Join-Path $staging 'published'
$package = Join-Path $staging 'package'
New-Item -ItemType Directory -Path $published, $package | Out-Null
try {
    dotnet publish $projects[0].FullName -c Release --self-contained false -o $published
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível compilar o plugin.' }
    $requiredAssemblies = if ($isV2) { @($manifest.module.assembly, $windowsRenderer.assembly) } else { @($manifest.entryAssembly) }
    foreach ($assembly in $requiredAssemblies) {
        if ([IO.Path]::GetFileName($assembly) -ne $assembly -or -not (Test-Path -LiteralPath (Join-Path $published $assembly))) {
            throw "A montagem declarada no manifesto não foi produzida: $assembly"
        }
    }

    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $package 'manifest.json')
    Get-ChildItem -LiteralPath $published -File | Where-Object {
        $_.Name -notin @('ClipDesk.PluginSdk.dll', 'ClipDesk.PluginSdk.Windows.dll') -and
        $_.Extension -ne '.pdb' -and $_.Extension -ne '.runtimeconfig.json'
    } | Copy-Item -Destination $package

    $archive = Join-Path $output ("$($manifest.id)-$($manifest.version).zip")
    if (Test-Path -LiteralPath $archive) { throw "A versão $($manifest.version) já foi empacotada: $archive" }
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $archive)
    $sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]@{
        package = $archive
        sha256 = $sha256
        feedEntry = [pscustomobject]@{
            manifestVersion = $manifest.manifestVersion
            id = $manifest.id
            name = $manifest.name
            description = $manifest.description
            version = $manifest.version
            url = "https://github.com/pedrommartini/ClipDesk/releases/download/plugins-$($manifest.version)/$($manifest.id)-$($manifest.version).zip"
            sha256 = $sha256
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
            defaultContent = $manifest.defaultContent
            platforms = $manifest.platforms
            stateVersion = $manifest.stateVersion
            module = $manifest.module
            renderers = $manifest.renderers
            permissions = $manifest.permissions
            capabilities = $manifest.capabilities
            networkHosts = $manifest.networkHosts
        }
    } | ConvertTo-Json -Depth 5
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
