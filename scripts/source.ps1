param([string]$Name = 'Nook-15.0-source')
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^Nook-[a-zA-Z0-9._-]+$') { throw 'Use a Nook-* package name without path separators.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskRelease = Join-Path $taskRoot 'artifacts/releases'
New-Item -ItemType Directory -Path $taskRelease -Force | Out-Null
$taskFiles = @('README.md', 'Nook.slnx', 'global.json', '.gitignore') | ForEach-Object { Get-Item -LiteralPath (Join-Path $taskRoot $_) }
foreach ($taskFolder in @('src', 'tests', 'scripts', 'docs')) {
    $taskFiles += Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskFolder) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}
Add-Type -AssemblyName System.IO.Compression
$taskStream = [IO.File]::Open((Join-Path $taskRelease "$Name.zip"), [IO.FileMode]::Create)
$taskZip = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in $taskFiles) {
        $taskRelative = $taskFile.FullName.Substring($taskRoot.Length + 1).Replace('\', '/')
        $taskEntry = $taskZip.CreateEntry("$Name/$taskRelative", [IO.Compression.CompressionLevel]::Optimal)
        $taskDestination = $taskEntry.Open()
        $taskSource = [IO.File]::OpenRead($taskFile.FullName)
        try { $taskSource.CopyTo($taskDestination) } finally { $taskSource.Dispose(); $taskDestination.Dispose() }
    }
} finally { $taskZip.Dispose(); $taskStream.Dispose() }
Write-Host "Source: $(Join-Path $taskRelease "$Name.zip")"

