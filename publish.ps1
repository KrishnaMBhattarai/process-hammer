# Builds BOTH distributables for Process Booster and refreshes the committed shareable exe.
#   1) Desktop\ProcessBooster\            runtime-dependent folder (small; needs .NET 8 Desktop Runtime)
#   2) Desktop\ProcessBooster-portable\   self-contained single file: ProcessBooster_SelfContained.exe (no prereqs)
# The self-contained exe is also copied into .\dist\ so it can be committed to git.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$proj = "src/ProcessBooster.App/ProcessBooster.App.csproj"
$desktop = [Environment]::GetFolderPath("Desktop")

Get-Process ProcessBooster, ProcessBooster_SelfContained -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "1/2  runtime-dependent folder -> $desktop\ProcessBooster" -ForegroundColor Cyan
dotnet publish $proj -c Release -o "$desktop\ProcessBooster" --nologo

Write-Host "2/2  self-contained single file -> $desktop\ProcessBooster-portable\ProcessBooster_SelfContained.exe" -ForegroundColor Cyan
$port = "$desktop\ProcessBooster-portable"
Remove-Item $port -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $port --nologo
Rename-Item "$port\ProcessBooster.exe" "ProcessBooster_SelfContained.exe" -Force

New-Item -ItemType Directory -Force -Path "$root\dist" | Out-Null
Copy-Item "$port\ProcessBooster_SelfContained.exe" "$root\dist\ProcessBooster_SelfContained.exe" -Force
Write-Host "Done. Shareable exe: dist\ProcessBooster_SelfContained.exe" -ForegroundColor Green
