param(
    [Parameter(Mandatory = $true)][string]$CredentialsPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$credentials = Get-Content -LiteralPath $CredentialsPath -Raw | ConvertFrom-Json
if (-not $credentials.installed -or $credentials.installed.client_id -notmatch '\.apps\.googleusercontent\.com$') {
    throw 'Selecione o JSON de um cliente OAuth do tipo Aplicativo para computador (Desktop).'
}
$devRoot = Join-Path $env:LOCALAPPDATA 'ClipDesk-Dev'
New-Item -ItemType Directory -Path $devRoot -Force | Out-Null
$clientPath = Join-Path $devRoot 'cloud-config.local.json'
$serverPath = Join-Path $projectRoot 'Server\appsettings.Development.local.json'
$client = if (Test-Path -LiteralPath $clientPath) {Get-Content -LiteralPath $clientPath -Raw | ConvertFrom-Json} else {[pscustomobject]@{ServerUrl = 'http://127.0.0.1:5278'}}
$client | Add-Member -NotePropertyName GoogleClientId -NotePropertyValue $credentials.installed.client_id -Force
$client | Add-Member -NotePropertyName GoogleDesktopClientSecret -NotePropertyValue $credentials.installed.client_secret -Force
$server = if (Test-Path -LiteralPath $serverPath) {Get-Content -LiteralPath $serverPath -Raw | ConvertFrom-Json} else {[pscustomobject]@{}}
$server | Add-Member -NotePropertyName Google -NotePropertyValue @{ClientId = $credentials.installed.client_id} -Force
$encoding = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($clientPath, ($client | ConvertTo-Json -Depth 10), $encoding)
[IO.File]::WriteAllText($serverPath, ($server | ConvertTo-Json -Depth 10), $encoding)
Write-Host 'Google configurado nos arquivos locais do DEV. Nenhuma credencial foi exibida.'
Write-Host 'Reinicie o servidor DEV e o ClipDesk DEV para carregar a configuração.'
