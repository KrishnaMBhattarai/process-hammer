<#
Builds both distributables into .\dist\ (gitignored):
  1) dist\ProcessHammer\                          runtime-dependent folder (needs .NET 8 Desktop Runtime)
  2) dist\ProcessHammer_SelfContained_vX.Y.Z.exe  self-contained single file (+ .sha256)

Pass -Release to also publish/refresh the matching GitHub release (requires the GitHub CLI, `gh`).
#>
param([switch]$Release)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$proj    = "src/ProcessHammer.App/ProcessHammer.App.csproj"
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
$dist    = Join-Path $root "dist"
$exeName = "ProcessHammer_SelfContained_v$version.exe"

# Stop any running instance so output files aren't locked.
Get-Process ProcessHammer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "1/2  runtime-dependent folder -> dist\ProcessHammer" -ForegroundColor Cyan
dotnet publish $proj -c Release -o "$dist\ProcessHammer" --nologo

Write-Host "2/2  self-contained single file -> dist\$exeName" -ForegroundColor Cyan
$portable = "$dist\_portable"
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $portable --nologo
Move-Item "$portable\ProcessHammer.exe" "$dist\$exeName" -Force
Remove-Item $portable -Recurse -Force -ErrorAction SilentlyContinue

$hash = (Get-FileHash "$dist\$exeName" -Algorithm SHA256).Hash.ToLower()
"$hash *$exeName" | Out-File "$dist\$exeName.sha256" -Encoding ascii

Write-Host "Done." -ForegroundColor Green
Write-Host "  exe:    dist\$exeName" -ForegroundColor Green
Write-Host "  sha256: $hash" -ForegroundColor Green

if ($Release) {
    $tag = "v$version"
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI 'gh' not found. Install it (https://cli.github.com) or run without -Release."
    }
    Write-Host "Publishing GitHub release $tag ..." -ForegroundColor Cyan
    $notes = @"
Process Hammer $tag — portable single-file build (bundles .NET; no install).

**SHA-256** ``$exeName``:
``````
$hash
``````
Verify with: ``Get-FileHash .\$exeName -Algorithm SHA256``
"@
    gh release view $tag *> $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        gh release create $tag "$dist\$exeName" "$dist\$exeName.sha256" --title $tag --notes $notes
    } else {
        gh release upload $tag "$dist\$exeName" "$dist\$exeName.sha256" --clobber
        gh release edit $tag --notes $notes   # keep the SHA in the notes current
    }
    Write-Host "Release $tag published." -ForegroundColor Green
}
