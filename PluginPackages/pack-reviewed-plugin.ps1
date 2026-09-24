param(
    [Parameter(Mandatory = $true)][string]$PluginDirectory,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$moduleDirectory = (Resolve-Path -LiteralPath $PluginDirectory).Path
$reviewPath = Join-Path $moduleDirectory 'UX-REVIEW.md'
if (-not (Test-Path -LiteralPath $reviewPath -PathType Leaf)) {
    throw 'Falta UX-REVIEW.md na pasta do plugin. Faça a revisão visual antes de empacotar.'
}

$rows = @(Get-Content -LiteralPath $reviewPath -Encoding UTF8 | Where-Object { $_ -match '^\|' })
$scenarios = @(
    'Mínimo quadrado',
    'Padrão',
    'Estreito e alto',
    'Largo e baixo',
    'Mínimo e padrão à distância',
    'Janela estreita',
    'Tema e destaque alternativos',
    'Output vazio, válido, longo e erro; cópia'
)

foreach ($scenario in $scenarios) {
    $matchingRows = @($rows | Where-Object { $_.Split('|')[1].Trim() -eq $scenario })
    if ($matchingRows.Count -ne 1) {
        throw "UX-REVIEW.md precisa conter exatamente uma linha para: $scenario"
    }
    $cells = @($matchingRows[0].Split('|') | Select-Object -Skip 1 -First 7 | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 7 -or @($cells | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "Preencha todas as colunas da revisão visual: $scenario"
    }
    if ($cells[6] -notmatch '^(?i:sim|aprovado|yes)$') {
        throw "Corrija o cenário antes de empacotar; a coluna Aprovado? deve ser Sim: $scenario"
    }
}

if ($ValidateOnly) {
    Write-Output 'Revisão visual preenchida e aprovada.'
    return
}

& (Join-Path $PSScriptRoot 'pack-plugin.ps1') -PluginDirectory $moduleDirectory -OutputDirectory $OutputDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
