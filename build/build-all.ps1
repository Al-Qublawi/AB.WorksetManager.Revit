<#
.SYNOPSIS
    Builds AB Worksets Manager for every supported Revit release, and runs the core self-test.

.DESCRIPTION
    Revit 2022-2024 run on .NET Framework 4.8, 2025-2026 on .NET 8, 2027 on .NET 10. Each release is
    compiled against pinned reference packages, so no Revit needs to be installed to build it.
    Output: artifacts\<Configuration>\Revit<year>\.

.EXAMPLE
    .\build\build-all.ps1
    .\build\build-all.ps1 -Configuration Debug -Versions 2022,2026
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Revit releases to build for. A string is split on commas, so "-Versions 2022,2024" also works when
    # the script is started with  powershell -File  (which hands arguments over as plain strings).
    [string[]] $Versions = @('2022', '2023', '2024', '2025', '2026', '2027'),

    [switch] $SkipTests
)

# "2022,2024" arrives as one string from  powershell -File ; a PowerShell prompt passes a real array.
$Versions = @($Versions | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } |
    Where-Object { $_ } | ForEach-Object {
        if ($_ -notmatch '^\d{4}$') { throw "'$_' is not a Revit release year." }
        [int] $_
    })

$ErrorActionPreference = 'Stop'
$repoRoot  = Split-Path -Parent $PSScriptRoot
$addinProj = Join-Path $repoRoot 'src\AB.WorksetManager.Addin\AB.WorksetManager.Addin.csproj'
$testProj  = Join-Path $repoRoot 'tests\AB.WorksetManager.Core.Tests\AB.WorksetManager.Core.Tests.csproj'

function Get-TargetFramework([int] $version) {
    if ($version -ge 2027) { return 'net10.0-windows' }
    if ($version -ge 2025) { return 'net8.0-windows' }
    return 'net48'
}

Write-Host ''
Write-Host 'AB Worksets Manager - build' -ForegroundColor Cyan
Write-Host ("Configuration : {0}" -f $Configuration)
Write-Host ''

$failed = @()

if (-not $SkipTests) {
    Write-Host '  TEST  core self-test' -ForegroundColor Yellow
    $output = & dotnet run --project $testProj -c $Configuration 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host "        $_" }
        Write-Host '        FAILED' -ForegroundColor Red
        $failed += 'tests'
    }
    else {
        Write-Host ('        ' + ($output | Select-Object -Last 1)) -ForegroundColor Green
    }
}

foreach ($version in $Versions) {
    $tfm = Get-TargetFramework $version
    Write-Host ("  BUILD Revit {0} ({1})" -f $version, $tfm) -ForegroundColor Yellow
    & dotnet build $addinProj -c $Configuration -f $tfm -p:RevitVersion=$version --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("        FAILED (exit {0})" -f $LASTEXITCODE) -ForegroundColor Red
        $failed += $version
    }
    else {
        Write-Host '        ok' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host ('Output : {0}' -f (Join-Path $repoRoot ("artifacts\{0}" -f $Configuration)))
if ($failed.Count) {
    Write-Host ('Failed : {0}' -f ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
exit 0
