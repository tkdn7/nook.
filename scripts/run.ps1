param([string]$SettingsDir)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$taskArgs = @('run', '--project', (Join-Path $taskRoot 'src/Nook.App/Nook.App.csproj'))
if ($SettingsDir) { $taskArgs += @('--', '--settings-dir', $SettingsDir) }
& $taskDotnet @taskArgs
