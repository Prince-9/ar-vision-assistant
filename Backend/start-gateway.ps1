$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-NetTCPConnection -LocalPort 8001 -State Listen -ErrorAction SilentlyContinue) {
    Write-Output 'Gateway is already listening on port 8001.'
    exit 0
}
Start-Process -FilePath (Join-Path $PSScriptRoot 'venv/Scripts/python.exe') -ArgumentList '-m','uvicorn','main:app','--host','127.0.0.1','--port','8001' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot 'gateway-current.stdout.log') -RedirectStandardError (Join-Path $PSScriptRoot 'gateway-current.stderr.log')
Write-Output 'Gateway started in the background on port 8001.'
