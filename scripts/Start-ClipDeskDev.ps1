param([switch]$OpenApp, [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appExe = Join-Path $projectRoot 'bin\Release\net8.0-windows\ClipDesk.exe'

if (-not $SkipBuild) {
    & dotnet build (Join-Path $projectRoot 'ClipDesk.csproj') -c Release '-p:ClipDeskEnvironment=Development' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação DEV falhou. Nenhum aplicativo foi iniciado.' }
}

if (-not (Test-Path -LiteralPath $appExe)) { throw 'Compile o aplicativo DEV antes de usar -SkipBuild.' }
Write-Host 'ClipDesk DEV pronto. Mesas compartilhadas usam Supabase.'
if ($OpenApp) { Start-Process -FilePath $appExe -WorkingDirectory $projectRoot }
