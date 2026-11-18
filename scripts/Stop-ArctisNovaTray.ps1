[CmdletBinding()]
param(
    [switch]$DisableStartup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($DisableStartup) {
    $projectRoot = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ArctisNovaTray.exe')) {
        $PSScriptRoot
    }
    else {
        Split-Path -Parent $PSScriptRoot
    }

    $candidates = @(
        (Join-Path $PSScriptRoot 'ArctisNovaTray.exe'),
        (Join-Path $projectRoot 'ArctisNovaTray.exe'),
        (Join-Path $projectRoot 'src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe'),
        (Join-Path $projectRoot 'src\arctis-nova-tray\bin\Debug\net9.0-windows\ArctisNovaTray.exe')
    )

    $targetExe = $null
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $targetExe = $candidate
            break
        }
    }

    if ($targetExe) {
        $null = (& $targetExe --disable-startup | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "ArctisNovaTray --disable-startup failed with exit code $LASTEXITCODE."
        }
        Write-Host 'Disabled Windows startup registration.'
    }
    else {
        Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ArctisNovaTray' -ErrorAction SilentlyContinue
        Write-Host 'Disabled Windows startup registration.'
    }
}

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
