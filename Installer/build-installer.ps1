param([string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$installerRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$payloadDirectory = [IO.Path]::GetFullPath((Join-Path $installerRoot "Payload\App"))
$payloadArchive = [IO.Path]::GetFullPath((Join-Path $installerRoot "Payload\ClipDeskPayload.zip"))
$setupOutput = [IO.Path]::GetFullPath((Join-Path $installerRoot "bin\Setup"))
$distribution = [IO.Path]::GetFullPath((Join-Path $projectRoot "dist"))

foreach ($path in @($payloadDirectory, $payloadArchive, $setupOutput, $distribution)) {
    if (-not $path.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho de build fora do projeto: $path"
    }
}

if (Test-Path -LiteralPath $payloadDirectory) { Remove-Item -LiteralPath $payloadDirectory -Recurse -Force }
if (Test-Path -LiteralPath $payloadArchive) { Remove-Item -LiteralPath $payloadArchive -Force }
if (Test-Path -LiteralPath $setupOutput) { Remove-Item -LiteralPath $setupOutput -Recurse -Force }
New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $payloadArchive) -Force | Out-Null
New-Item -ItemType Directory -Path $distribution -Force | Out-Null

dotnet publish (Join-Path $projectRoot "ClipDesk.csproj") -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $payloadDirectory
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o ClipDesk." }

Compress-Archive -Path (Join-Path $payloadDirectory "*") -DestinationPath $payloadArchive -CompressionLevel Optimal

dotnet publish (Join-Path $installerRoot "ClipDesk.Installer.csproj") -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $setupOutput
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o instalador." }

Copy-Item -LiteralPath (Join-Path $setupOutput "ClipDesk-Setup.exe") -Destination (Join-Path $distribution "ClipDesk-Setup.exe") -Force
Copy-Item -LiteralPath $payloadArchive -Destination (Join-Path $distribution "ClipDesk-Windows-x64.zip") -Force
Get-Item (Join-Path $distribution "ClipDesk-Setup.exe"), (Join-Path $distribution "ClipDesk-Windows-x64.zip")
