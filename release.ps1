# Tags the current commit and pushes the tag; GitHub Actions then builds InkspirePanel.exe and attaches it to the release.
#   .\release.ps1 -Version 1.2.3
param(
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = "Stop"
$tag = "v" + $Version.TrimStart('v')

if (-not (git rev-parse --is-inside-work-tree 2>$null)) { throw "Not inside a git repository." }
$dirty = git status --porcelain
if ($dirty) { throw "Commit or stash your changes first:`n$dirty" }

Write-Host "Running the tests before tagging $tag ..."
dotnet test (Join-Path $PSScriptRoot "tests\InkspirePanel.Tests\InkspirePanel.Tests.csproj") --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not tagging." }

git tag -a $tag -m "Inkspire Control Panel $tag"
git push origin $tag
Write-Host "Pushed $tag. Watch the 'Release InkspirePanel.exe' workflow; the exe appears on the release page when it finishes."
