<#
.SYNOPSIS
    Debug build of AB Worksets Manager for live testing inside a running Revit (tools\revit-bridge).

.DESCRIPTION
    Builds in Debug (which includes Diagnostics\DevHarness) with assembly version 1.0.0.<N> and copies
    the DLLs to %LOCALAPPDATA%\Temp\ABWorksetDev\v<N>. A running Revit never unloads an assembly, so
    every rebuild loaded into the same session needs a new N; the snippets load by version, from bytes.

.EXAMPLE
    .\tools\devbuild.ps1 -N 1 -RevitVersion 2026
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [int] $N,
    [ValidateSet(2022, 2023, 2024, 2025, 2026, 2027)] [int] $RevitVersion = 2026
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'src\AB.WorksetManager.Addin\AB.WorksetManager.Addin.csproj'
$tfm = if ($RevitVersion -ge 2027) { 'net10.0-windows' } elseif ($RevitVersion -ge 2025) { 'net8.0-windows' } else { 'net48' }
$dest = Join-Path $env:LOCALAPPDATA "Temp\ABWorksetDev\v$N"

Write-Host "Debug build 1.0.0.$N for Revit $RevitVersion ($tfm)" -ForegroundColor Cyan
& dotnet build $project -c Debug -f $tfm "-p:RevitVersion=$RevitVersion" "-p:AssemblyVersion=1.0.0.$N" "-p:FileVersion=1.0.0.$N" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.' -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $repoRoot "artifacts\Debug\Revit$RevitVersion\AB.WorksetManager.*.dll") -Destination $dest -Force
Write-Host "Copied to $dest" -ForegroundColor Green
Write-Host "Set  string v = `"$N`";  at the top of the tools\revit-bridge snippet you send."
