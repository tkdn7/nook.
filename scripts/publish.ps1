param([string]$Name = 'Nook-15.0-win-x64')
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^Nook-[a-zA-Z0-9._-]+$') { throw 'Use a Nook-* package name without path separators.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$taskOutput = Join-Path $taskRoot "artifacts/releases/$Name"
if (Test-Path -LiteralPath $taskOutput) {
    $taskUnexpected = Get-ChildItem -LiteralPath $taskOutput | Where-Object { $_.Name -notin @('Nook.exe', 'docs') }
    if ($taskUnexpected) { throw 'Output contains files from a different packaging layout. Choose a new -Name.' }
}
Push-Location $taskRoot
try {
    & $taskDotnet publish src/Nook.App/Nook.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $taskDocs = Join-Path $taskOutput 'docs'
    $taskLicenses = Join-Path $taskDocs 'licenses'
    New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskDocs 'README.md')
    foreach ($taskDoc in @('VALIDATION.md', 'DESIGN_DIRECTIONS.md', 'AUDIO_OUTPUT.md', 'APPROVED_IMPROVEMENTS.md', 'IMPLEMENTATION_REPORT.md')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot "docs/$taskDoc") -Destination (Join-Path $taskDocs $taskDoc)
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'src/Nook.App/Assets/Icons/LICENSE.txt') -Destination (Join-Path $taskLicenses 'Fluent-System-Icons.txt')
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/Nook.App/Assets/Fonts') -Filter '*.txt' | Copy-Item -Destination $taskLicenses
    Compress-Archive -LiteralPath $taskOutput -DestinationPath "$taskOutput.zip" -Force
    Write-Host "Package: $taskOutput.zip"
} finally { Pop-Location }

