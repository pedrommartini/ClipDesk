param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appExe = Join-Path $projectRoot 'bin\Release\net8.0-windows\ClipDesk.exe'

if (-not $SkipBuild) {
    & dotnet build (Join-Path $projectRoot 'ClipDesk.csproj') -c Release '-p:ClipDeskEnvironment=Development' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação DEV falhou. A segunda instância não foi iniciada.' }
}
if (-not (Test-Path -LiteralPath $appExe)) { throw 'Compile o ClipDesk DEV antes de usar -SkipBuild.' }

Start-Process -FilePath $appExe -WorkingDirectory $projectRoot -ArgumentList '--second-client'
Write-Host 'Segunda instância iniciada. Dados: %LocalAppData%\ClipDesk-Dev-Test2. Nuvem: Supabase.'
