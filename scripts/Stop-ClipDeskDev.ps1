$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$serverExe = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Server\bin\Release\net10.0\ClipDesk.Server.exe'))
$pidPath = Join-Path $projectRoot 'Server\data\server.pid'
$tunnelPidPath = Join-Path $projectRoot 'Server\data\tunnel.pid'
if (Test-Path -LiteralPath $tunnelPidPath) {
    $tunnelProcessId = [int](Get-Content -LiteralPath $tunnelPidPath -Raw)
    $tunnelProcess = Get-Process -Id $tunnelProcessId -ErrorAction SilentlyContinue
    if ($tunnelProcess) {
        if ($tunnelProcess.ProcessName -ne 'cloudflared') {throw 'O PID registrado para o túnel pertence a outro processo. Nada foi encerrado.'}
        Stop-Process -InputObject $tunnelProcess
        $tunnelProcess.WaitForExit(5000) | Out-Null
    }
    Remove-Item -LiteralPath $tunnelPidPath
}
if (-not (Test-Path -LiteralPath $pidPath)) {Write-Host 'Nenhum servidor iniciado pelo script foi registrado.'; return}
$serverProcessId = [int](Get-Content -LiteralPath $pidPath -Raw)
$serverProcess = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
if ($serverProcess) {
    if ($serverProcess.Path -ne $serverExe) {throw 'O PID registrado pertence a outro processo. Nada foi encerrado.'}
    Stop-Process -InputObject $serverProcess
    $serverProcess.WaitForExit(5000) | Out-Null
}
Remove-Item -LiteralPath $pidPath
Write-Host 'Servidor DEV encerrado. Os dados foram mantidos.'
