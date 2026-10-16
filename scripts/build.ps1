param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
Push-Location $taskRoot
try {
    & $taskDotnet build src/Nook.App/Nook.App.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $taskDotnet run --project tests/Nook.Tests/Nook.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $taskDotnet run --project tests/Nook.UiTests/Nook.UiTests.csproj -c Release -- artifacts/qa
    if ($LASTEXITCODE -ne 0) { throw 'UI render checks failed.' }
    if ($Publish) {
        & (Join-Path $PSScriptRoot 'publish.ps1')
    }
} finally { Pop-Location }
