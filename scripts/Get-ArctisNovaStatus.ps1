[CmdletBinding()]
param(
    [switch]$RawJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

if (-not $targetExe) {
    $csprojPath = Join-Path $projectRoot 'src\arctis-nova-tray\ArctisNovaTray.csproj'
    if (-not (Test-Path -LiteralPath $csprojPath)) {
        throw 'ArctisNovaTray.exe was not found in the release directory or build output.'
    }
    & dotnet build $csprojPath -c Release --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
    $targetExe = Join-Path $projectRoot 'src\arctis-nova-tray\bin\Release\net9.0-windows\ArctisNovaTray.exe'
}

# Piping through Out-String ensures PowerShell waits synchronously for the WinExe subsystem child process.
$jsonText = (& $targetExe --status-json | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "ArctisNovaTray --status-json failed with exit code $LASTEXITCODE."
}

if ([string]::IsNullOrWhiteSpace($jsonText)) {
    throw 'ArctisNovaTray --status-json returned empty output.'
}

if ($RawJson) {
    Write-Output $jsonText
}
else {
    $jsonText | ConvertFrom-Json
}
