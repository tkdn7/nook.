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
        & $taskDotnet publish src/Nook.App/Nook.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/Nook-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item -LiteralPath README.md -Destination artifacts/Nook-win-x64/README.md
        Copy-Item -LiteralPath VALIDATION.md -Destination artifacts/Nook-win-x64/VALIDATION.md
        Copy-Item -LiteralPath DESIGN_DIRECTIONS.md -Destination artifacts/Nook-win-x64/DESIGN_DIRECTIONS.md
        Compress-Archive -Path artifacts/Nook-win-x64 -DestinationPath artifacts/Nook-win-x64.zip -Force
    }
} finally { Pop-Location }
