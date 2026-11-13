[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$existing = @(Get-Process -Name 'ArctisNovaTray' -ErrorAction SilentlyContinue)
if ($existing.Count -eq 0) {
    Write-Host 'ArctisNovaTray is not running.'
    exit 0
}

# Signal graceful shutdown event first so NotifyIcon is removed cleanly without leaving a ghost tray icon.
try {
    $shutdownEvent = [System.Threading.EventWaitHandle]::OpenExisting('Local\ArctisNovaTray_Shutdown_v1')
    try {
        [void]$shutdownEvent.Set()
    }
    finally {
        $shutdownEvent.Dispose()
    }
}
catch {
    # Event handle may not exist if process is still initializing; fall through to process wait/stop.
}

$deadline = (Get-Date).AddMilliseconds(2000)
while ((Get-Date) -lt $deadline) {
    if (@(Get-Process -Name 'ArctisNovaTray' -ErrorAction SilentlyContinue).Count -eq 0) {
        Write-Host 'ArctisNovaTray stopped cleanly.'
        exit 0
    }
    Start-Sleep -Milliseconds 100
}

$remaining = @(Get-Process -Name 'ArctisNovaTray' -ErrorAction SilentlyContinue)
foreach ($proc in $remaining) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

Write-Host 'ArctisNovaTray stopped.'
