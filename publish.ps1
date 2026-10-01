# Builds the single-file, self-contained InkspirePanel.exe into .\dist\
# -Version stamps the exe (panel status bar, panel.json Versions.Panel); releases take it from the git tag.
param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "inkspire-installer.csproj"
$out = Join-Path $PSScriptRoot "dist"
$props = @("-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true")
if ($Version) { $props += "-p:Version=$Version" }
dotnet publish $proj -c $Configuration -r win-x64 --self-contained true @props -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
Get-ChildItem $out -Filter *.exe | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
