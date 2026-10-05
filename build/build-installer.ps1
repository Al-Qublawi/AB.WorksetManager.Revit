<#
.SYNOPSIS
    Produces dist\AB.WorksetManager-<version>.msi - one Windows Installer package for Revit 2022-2027.

.DESCRIPTION
    1. Builds the add-in for every Revit release (build-all.ps1), core self-test included.
    2. Stages each release's two assemblies.
    3. Builds the .msi with the AB Adv Tools kit from installer\WorksetManager.msi.psd1.

    The package runs no code of its own, so company PCs with Defender's attack surface reduction
    rules install it like any .msi. Only me (default) needs no administrator rights.

.EXAMPLE
    .uilduild-installer.ps1
    .uilduild-installer.ps1 -DryRun      # then show what the package would do here, changing nothing
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Revit releases to build for. A string is split on commas, so "-Versions 2022,2024" also works when
    # the script is started with  powershell -File  (which hands arguments over as plain strings).
    [string[]] $Versions = @('2022', '2023', '2024', '2025', '2026', '2027'),

    [switch] $SkipBuild,
    [switch] $DryRun
)

$Versions = @($Versions | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } |
    Where-Object { $_ } | ForEach-Object {
        if ($_ -notmatch '^\d{4}$') { throw "'$_' is not a Revit release year." }
        [int] $_
    })

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot ("artifacts\{0}" -f $Configuration)
$stage     = Join-Path $repoRoot 'obj\msi\payload'
$distDir   = Join-Path $repoRoot 'dist'
$kitMsi    = Join-Path $repoRoot 'shared\ABAdvTools\msi'

function Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "   [ok]   $t" -ForegroundColor Green }
function Info($t) { Write-Host "          $t" -ForegroundColor DarkGray }
function Die($t)  { Write-Host "   [fail] $t" -ForegroundColor Red; exit 1 }

$version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

Write-Host ''
Write-Host "  AB Worksets Manager $version - installer builder" -ForegroundColor White
Write-Host '  by Abdullah Lotfy' -ForegroundColor DarkGray

if (-not $SkipBuild) {
    Step 'Building the add-in for each Revit release'
    & (Join-Path $PSScriptRoot 'build-all.ps1') -Configuration $Configuration -Versions $Versions
    if ($LASTEXITCODE -ne 0) { Die 'Build failed.' }
}

Step 'Staging the package contents'
if (Test-Path $stage) { [System.IO.Directory]::Delete($stage, $true) }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$packed = @()
foreach ($revit in $Versions) {
    $source = Join-Path $artifacts ("Revit{0}" -f $revit)
    if (-not (Test-Path (Join-Path $source 'AB.WorksetManager.Addin.dll'))) { Info "Revit $revit not built - skipped."; continue }
    # Only our own assemblies: a stray Revit API DLL beside an add-in causes type-identity bugs.
    $to = New-Item -ItemType Directory -Force -Path (Join-Path $stage ("Revit{0}" -f $revit))
    Get-ChildItem $source -File |
        Where-Object { $_.Name -like 'AB.WorksetManager.*' -and $_.Extension -eq '.dll' } |
        ForEach-Object { Copy-Item $_.FullName -Destination $to.FullName }
    Ok ("Revit {0}  ({1} files)" -f $revit, (Get-ChildItem $to.FullName -Recurse -File).Count)
    $packed += $revit
}
if ($packed.Count -eq 0) { Die "No add-in build output found in '$artifacts'. Run without -SkipBuild." }

Step 'Building the .msi'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$built = & (Join-Path $kitMsi 'New-AdvToolsMsi.ps1') `
    -Definition (Join-Path $repoRoot 'installer\WorksetManager.msi.psd1') `
    -Payload $stage -Version $version -OutputDirectory $distDir `
    -WorkDirectory (Join-Path $repoRoot 'obj\msi\work')
$msi = $built.Path

# Only one installer is ever left in dist\, so nobody hands out a stale one.
Get-ChildItem $distDir -File |
    Where-Object { $_.FullName -ne $msi -and $_.Name -like 'AB.WorksetManager*' } |
    ForEach-Object { [System.IO.File]::Delete($_.FullName) }

$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
$identity = @(
    'AB Worksets Manager - installer identity'
    'Publisher      : Abdullah Lotfy'
    ('Version        : {0}' -f $version)
    ('File           : {0}' -f (Split-Path -Leaf $msi))
    ('Size           : {0} bytes' -f (Get-Item $msi).Length)
    ('SHA256         : {0}' -f $hash)
    ''
    'This is a Windows Installer package with no custom actions that run code. Defender''s attack'
    'surface reduction rule "Block executable files from running unless they meet a prevalence, age,'
    'or trusted list criteria" (01443614-cd74-433a-b99e-2ecdc07bfc25) applies to executables, not to'
    'it. If a policy still blocks it, allow it by the SHA256 above.'
) -join "`r`n"
[System.IO.File]::WriteAllText((Join-Path $distDir 'AB.WorksetManager.allowlist.txt'), $identity + "`r`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host '  Installer ready' -ForegroundColor Green
Write-Host ("  {0}   ({1:N1} MB)" -f $msi, ((Get-Item $msi).Length / 1MB))
Write-Host ("  Revit releases inside: {0}" -f ($packed -join ', '))
Write-Host ''
Write-Host '  Double-click it, or deploy silently:'
Write-Host "    msiexec /i AB.WorksetManager-$version.msi /qn                 (Only me)"
Write-Host "    msiexec /i AB.WorksetManager-$version.msi /qn ALLUSERS=1      (Everyone, as administrator)"
Write-Host "    msiexec /x AB.WorksetManager-$version.msi /qn"
Write-Host ''

if ($DryRun) {
    & (Join-Path $kitMsi 'Test-AdvToolsMsi.ps1') -Msi $msi -WorkDirectory (Join-Path $repoRoot 'obj\msi\dryrun')
}
