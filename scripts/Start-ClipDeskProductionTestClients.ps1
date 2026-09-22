param([string]$AppDirectory = 'E:\AI\Codex\Clipdesk_test\Testes\Production-2026-09-21-drag-outline-v7')

$ErrorActionPreference = 'Stop'
$appExe = Join-Path ([IO.Path]::GetFullPath($AppDirectory)) 'ClipDesk.exe'
if (-not (Test-Path -LiteralPath $appExe)) { throw "Pacote Production de teste não encontrado: $appExe" }

foreach ($number in 1, 2) {
    Start-Process -FilePath $appExe -WorkingDirectory $AppDirectory -ArgumentList "--test-client=$number"
}
Write-Host 'Duas janelas Production abertas. Cada uma usa sessão local isolada e ambas usam Supabase.'


