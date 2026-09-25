# Builds BOTH distributables for Process Booster and refreshes the committed shareable exe + checksum.
#   1) Desktop\ProcessBooster\            runtime-dependent folder (small; needs .NET 8 Desktop Runtime)
#   2) Desktop\ProcessBooster-portable\   self-contained single file, versioned + no prerequisites
# The versioned exe and its SHA-256 are copied into .\dist\ (gitignored) for uploading to a GitHub Release.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$proj = "src/ProcessBooster.App/ProcessBooster.App.csproj"
$desktop = [Environment]::GetFolderPath("Desktop")
$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
$exeName = "ProcessBooster_SelfContained_v$version.exe"
$folderExeName = "ProcessBooster_v$version.exe"

Get-Process ProcessBooster, "ProcessBooster_SelfContained_v$version" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "1/2  runtime-dependent folder -> $desktop\ProcessBooster\$folderExeName" -ForegroundColor Cyan
dotnet publish $proj -c Release -o "$desktop\ProcessBooster" --nologo
# Version-suffix the apphost exe too (it still launches ProcessBooster.dll by assembly name).
Rename-Item "$desktop\ProcessBooster\ProcessBooster.exe" $folderExeName -Force

Write-Host "2/2  self-contained single file -> $desktop\ProcessBooster-portable\$exeName" -ForegroundColor Cyan
$port = "$desktop\ProcessBooster-portable"
Remove-Item $port -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $port --nologo
Rename-Item "$port\ProcessBooster.exe" $exeName -Force

New-Item -ItemType Directory -Force -Path "$root\dist" | Out-Null
Copy-Item "$port\$exeName" "$root\dist\$exeName" -Force
$hash = (Get-FileHash "$root\dist\$exeName" -Algorithm SHA256).Hash.ToLower()
"$hash *$exeName" | Out-File "$root\dist\$exeName.sha256" -Encoding ascii
Write-Host "Done." -ForegroundColor Green
Write-Host "  exe:    dist\$exeName" -ForegroundColor Green
Write-Host "  sha256: $hash" -ForegroundColor Green
