<#
.SYNOPSIS
  Publishes Cairo Plus for one build profile and produces a portable zip and (optionally) an Inno Setup installer.

.EXAMPLE
  ./tools/packaging/Build-Release.ps1 -Profile x64 -Version 0.5.0 -OutputDir artifacts

  Profiles match Cairo Desktop/Cairo Desktop/Properties/PublishProfiles: x64, x86, arm64, x64_net480, x86_net480.
  Windows only (WPF). Needs the .NET SDK; the installer step needs Inno Setup's iscc.exe on PATH.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('x64', 'x86', 'arm64', 'x64_net480', 'x86_net480')][string]$Profile,
  [Parameter(Mandatory)][string]$Version,
  [string]$OutputDir = 'artifacts',
  [switch]$SkipInstaller,
  [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'

$map = @{
  x64        = @{ Framework = 'net6.0-windows';  Platform = 'x64';   Arch = 'x64';   Publish = 'publish-x64' }
  x86        = @{ Framework = 'net6.0-windows';  Platform = 'AnyCPU'; Arch = 'x86';   Publish = 'publish-x86' }
  arm64      = @{ Framework = 'net10.0-windows'; Platform = 'ARM64'; Arch = 'arm64'; Publish = 'publish-arm64' }
  x64_net480 = @{ Framework = 'net480';          Platform = 'x64';   Arch = 'x64';   Publish = 'publish-x64' }
  x86_net480 = @{ Framework = 'net480';          Platform = 'AnyCPU'; Arch = 'x86';   Publish = 'publish-x86' }
}
$p = $map[$Profile]
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repo 'Cairo Desktop\Cairo Desktop'
$publishDir = Join-Path $project "bin\Release\$($p.Framework)\$($p.Publish)"
$suffix = if ($Profile -like '*_net480') { '-net480' } else { '' }
$name = "CairoPlus-$Version-$($p.Arch)$suffix"
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = Resolve-Path $OutputDir

if (-not $SkipPublish) {
  Push-Location $project
  try {
    dotnet publish "-p:PublishProfile=$Profile" "-p:Platform=$($p.Platform)" -f $p.Framework
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
  } finally { Pop-Location }
}
if (-not (Test-Path (Join-Path $publishDir 'CairoDesktop.exe'))) {
  throw "CairoDesktop.exe not found in $publishDir"
}

# Portable zip: the publish folder under a single top-level directory, plus license and a short readme.
$stage = Join-Path $env:TEMP "cairo-portable-$([guid]::NewGuid().ToString('N'))"
$root = Join-Path $stage $name
New-Item -ItemType Directory -Force $root | Out-Null
Copy-Item -Recurse -Force (Join-Path $publishDir '*') $root
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $root 'LICENSE.txt')
@"
Cairo Plus $Version ($($p.Arch)$suffix) - portable

Unzip anywhere and run CairoDesktop.exe. Only one copy of Cairo can run at a time:
exit any installed Cairo first (Cairo menu > Exit Cairo).
Settings are stored in %LOCALAPPDATA%\Cairo Desktop and are shared with installed copies.
"@ | Set-Content -Encoding ASCII (Join-Path $root 'README-portable.txt')
$zip = Join-Path $OutputDir "$name-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $root -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Host "Portable zip: $zip"

if (-not $SkipInstaller) {
  $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
  $iscc = if ($cmd) { $cmd.Source } else { "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
  if (-not (Test-Path $iscc)) { throw 'Inno Setup (iscc.exe) not found. Install it: choco install innosetup' }
  $baseVersion = ($Version -replace '^v', '') -replace '[-+].*$', ''
  & $iscc "/DAppVersion=$baseVersion" "/DArch=$($p.Arch)" "/DSourceDir=$publishDir" "/DOutputDir=$OutputDir" (Join-Path $repo 'Installer\inno\CairoPlus.iss')
  if ($LASTEXITCODE -ne 0) { throw "iscc failed ($LASTEXITCODE)" }
  # Keep .NET 4.8 builds distinguishable from the self-contained ones.
  $built = Join-Path $OutputDir "CairoPlusSetup_$($p.Arch).exe"
  $final = Join-Path $OutputDir "CairoPlus-$Version-$($p.Arch)$suffix-setup.exe"
  if (Test-Path $final) { Remove-Item $final }
  Move-Item $built $final
  Write-Host "Installer: $final"
}

# SHA256SUMS next to the artifacts (appended per profile; sorted and de-duplicated at the end).
$sums = Join-Path $OutputDir 'SHA256SUMS.txt'
$lines = @()
if (Test-Path $sums) { $lines += Get-Content $sums }
Get-ChildItem $OutputDir -File | Where-Object { $_.Name -like "$name*" -and $_.Extension -in '.zip', '.exe' } | ForEach-Object {
  $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $lines += "$h  $($_.Name)"
}
$lines | Sort-Object -Unique | Set-Content -Encoding ASCII $sums
Write-Host "Checksums: $sums"
