param([Parameter(Mandatory=$true)][string]$Executable, [string]$Label = 'idle', [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = (Resolve-Path -LiteralPath $Executable).Path
$taskArea = Join-Path $taskRoot 'artifacts/qa/approved-performance'
New-Item -ItemType Directory -Path $taskArea -Force | Out-Null
$taskSettings = Join-Path $taskArea ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskSettings | Out-Null
Set-Content -LiteralPath (Join-Path $taskSettings 'settings.json') -Value '{"Version":1,"Widgets":[],"StartupPromptShown":true}' -Encoding UTF8
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--background','--settings-dir',('"'+$taskSettings+'"')) -WindowStyle Hidden -PassThru
$taskStarted = $taskProcess.StartTime
try {
    Start-Sleep -Seconds 5
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw 'Isolated process exited during warmup.' }
    $taskCpuStart = $taskProcess.TotalProcessorTime.TotalMilliseconds
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    while ($taskWatch.Elapsed.TotalSeconds -lt $Seconds) {
        Start-Sleep -Milliseconds 250
        $taskProcess.Refresh()
        if ($taskProcess.HasExited) { throw 'Isolated process exited during measurement.' }
    }
    $taskWatch.Stop()
    $taskProcess.Refresh()
    $taskResult = [pscustomobject]@{
        Label=$Label; Executable=$taskExe; Scenario='Empty layout, main hidden, isolated settings';
        ElapsedSeconds=[Math]::Round($taskWatch.Elapsed.TotalSeconds,3);
        CpuMilliseconds=[Math]::Round($taskProcess.TotalProcessorTime.TotalMilliseconds-$taskCpuStart,3);
        PrivateBytes=$taskProcess.PrivateMemorySize64; WorkingSetBytes=$taskProcess.WorkingSet64
    }
    $taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArea ($Label+'.json')) -Encoding UTF8
    $taskResult | ConvertTo-Json -Compress
} finally {
    # Stop only the process launched above, after verifying its identity and isolated arguments.
    if (-not $taskProcess.HasExited) {
        $taskNative = Get-CimInstance Win32_Process -Filter ('ProcessId='+$taskProcess.Id)
        if ($taskProcess.StartTime -eq $taskStarted -and $taskNative.ExecutablePath -eq $taskExe -and $taskNative.CommandLine.Contains($taskSettings)) {
            Stop-Process -Id $taskProcess.Id
        } else { throw 'QA process identity changed; refusing to stop it.' }
    }
}
