[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Build,
    [switch]$Restart,
    [switch]$Silent,
    [switch]$AllowHeadless,
    [switch]$EnableStartup,
    [switch]$DisableStartup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($EnableStartup -and $DisableStartup) {
    throw 'Cannot specify both -EnableStartup and -DisableStartup in the same call.'
}

if ($Restart) {
    $stopScript = Join-Path $PSScriptRoot 'Stop-ArctisNovaTray.ps1'
    if (-not (Test-Path -LiteralPath $stopScript)) {
        $stopScript = Join-Path $PSScriptRoot 'scripts\Stop-ArctisNovaTray.ps1'
    }
    & $stopScript
}

$projectRoot = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ArctisNovaTray.exe')) {
    $PSScriptRoot
}
else {
    Split-Path -Parent $PSScriptRoot
}

$adjacentExe = Join-Path $PSScriptRoot 'ArctisNovaTray.exe'
$packagedRootExe = Join-Path $projectRoot 'ArctisNovaTray.exe'
$csprojPath = Join-Path $projectRoot 'src\arctis-nova-tray\ArctisNovaTray.csproj'
$releaseExe = Join-Path $projectRoot 'src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe'
$debugExe = Join-Path $projectRoot 'src\arctis-nova-tray\bin\Debug\net9.0-windows\ArctisNovaTray.exe'

$targetExe = $null

if ($Build) {
    if (-not (Test-Path -LiteralPath $csprojPath)) {
        throw "Source project not found at '$csprojPath'; cannot run -Build from a binary-only package."
    }
    Write-Host "Building ArctisNovaTray ($Configuration)..."
    & dotnet build $csprojPath -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
    $targetExe = if ($Configuration -eq 'Release') { $releaseExe } else { $debugExe }
}
else {
    $candidates = @(
        $adjacentExe,
        $packagedRootExe,
        $(if ($Configuration -eq 'Release') { $releaseExe } else { $debugExe }),
        $releaseExe,
        $debugExe
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $targetExe = $candidate
            break
        }
    }

    if (-not $targetExe) {
        if (-not (Test-Path -LiteralPath $csprojPath)) {
            throw 'ArctisNovaTray.exe was not found in the release directory or build output.'
        }
        Write-Host "Building ArctisNovaTray ($Configuration)..."
        & dotnet build $csprojPath -c $Configuration --nologo -v quiet
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
        $targetExe = if ($Configuration -eq 'Release') { $releaseExe } else { $debugExe }
    }
}

$trayArgs = [System.Collections.Generic.List[string]]::new()
if ($Silent) {
    $trayArgs.Add('--silent')
}
if ($AllowHeadless) {
    $trayArgs.Add('--allow-headless')
}

if ($EnableStartup) {
    $startupOut = (& $targetExe --enable-startup | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "ArctisNovaTray --enable-startup failed with exit code $LASTEXITCODE."
    }
    $startupInfo = $startupOut | ConvertFrom-Json
    Write-Host "Enabled Windows startup registration: $($startupInfo.command)"
}
elseif ($DisableStartup) {
    $null = (& $targetExe --disable-startup | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "ArctisNovaTray --disable-startup failed with exit code $LASTEXITCODE."
    }
    Write-Host 'Disabled Windows startup registration.'
}

$startParams = @{
    FilePath         = $targetExe
    WorkingDirectory = $projectRoot
    PassThru         = $true
}
if ($trayArgs.Count -gt 0) {
    $startParams['ArgumentList'] = $trayArgs.ToArray()
}

$launched = Start-Process @startParams
Start-Sleep -Milliseconds 600

if ($launched.HasExited -and $launched.ExitCode -eq 3) {
    throw 'No interactive Windows taskbar (explorer.exe Shell_TrayWnd) is reachable in this desktop or sandbox (ExitCode 3). Run Start-ArctisNovaTray.ps1 from your interactive Windows desktop terminal, or pass -AllowHeadless for automated tests.'
}

$running = @(Get-Process -Name 'ArctisNovaTray' -ErrorAction SilentlyContinue)
if ($launched.HasExited -and $launched.ExitCode -eq 0 -and $running.Count -ge 1) {
    Write-Host "ArctisNovaTray is already running (PID $($running[0].Id)); brought the live Status Card to the front near the system tray (use -Restart to relaunch)."
}
elseif ($running.Count -ge 1) {
    if ($Silent) {
        Write-Host "ArctisNovaTray started silently (PID $($running[0].Id), Binary: $targetExe)."
    }
    else {
        Write-Host "ArctisNovaTray started (PID $($running[0].Id), Binary: $targetExe). Status Card opened near the system tray (tip: check the taskbar '^' overflow menu to pin the tray icon)."
    }
}
else {
    throw "ArctisNovaTray exited unexpectedly (ExitCode: $($launched.ExitCode))."
}
