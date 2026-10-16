param([string]$Executable = 'artifacts/releases/Nook-15.0-win-x64/Nook.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = (Resolve-Path -LiteralPath (Join-Path $taskRoot $Executable)).Path
$taskSettings = Join-Path $taskRoot ('artifacts/qa/v15-live/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskSettings -Force | Out-Null
$taskModels = @()
foreach ($taskKind in @('Cpu', 'Clock', 'Media', 'Activity')) {
    $taskModels += @{ Id=[Guid]::NewGuid().ToString(); Kind=$taskKind; Width=300; Height=200; Left=100+($taskModels.Count*310); Top=100; Visible=$true }
}
@{ Version=1; Widgets=$taskModels; StartupPromptShown=$true; MainTheme='Light'; Theme='Light' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskSettings 'settings.json') -Encoding UTF8
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class NookPackageWindows {
    public delegate bool Callback(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder value, int length);
    public static string[] VisibleTitles(int target) {
        var result = new List<string>();
        EnumWindows((hwnd,state) => { uint pid; GetWindowThreadProcessId(hwnd,out pid); if(pid==target && IsWindowVisible(hwnd)) { var text=new StringBuilder(256); GetWindowText(hwnd,text,256); if(text.Length>0)result.Add(text.ToString()); } return true; },IntPtr.Zero);
        return result.ToArray();
    }
}
'@
$taskArgs = @('--background', '--settings-dir', ('"'+$taskSettings+'"'))
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
$taskStarted = $taskProcess.StartTime
try {
    Start-Sleep -Seconds 4
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw 'Packaged app exited before verification.' }
    $taskTitles = [NookPackageWindows]::VisibleTitles($taskProcess.Id)
    foreach ($taskTitle in @('Nook · CPU','Nook · Clock','Nook · Media','Nook · Activity')) {
        if ($taskTitles -notcontains $taskTitle) { throw ('Missing packaged widget: '+$taskTitle) }
    }
    $taskSecond = Start-Process -FilePath $taskExe -ArgumentList @('--settings-dir', ('"'+$taskSettings+'"')) -WindowStyle Hidden -PassThru
    if (-not $taskSecond.WaitForExit(10000)) { throw 'Duplicate instance did not exit.' }
    Start-Sleep -Milliseconds 400
    if ([NookPackageWindows]::VisibleTitles($taskProcess.Id) -notcontains 'Nook') { throw 'Duplicate launch did not restore main window.' }
    if (Test-Path -LiteralPath (Join-Path $taskSettings 'errors.log')) { throw 'Packaged app wrote errors.log.' }
    if (Get-ChildItem -LiteralPath $taskSettings -Filter '*.corrupt*') { throw 'Packaged app quarantined its settings.' }
    $taskSaved = Get-Content -LiteralPath (Join-Path $taskSettings 'settings.json') -Raw | ConvertFrom-Json
    if ($taskSaved.Widgets.Count -ne 4) { throw 'Packaged settings did not retain widgets.' }
    [pscustomobject]@{ Version=(Get-Item -LiteralPath $taskExe).VersionInfo.ProductVersion; VisibleWidgets=$taskTitles; DuplicateExited=$true; MainRestored=$true; SavedWidgets=$taskSaved.Widgets.Count; Errors=$false; Settings=$taskSettings } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/qa/v15-live/package-live.json') -Encoding UTF8
    Write-Host 'PASS packaged runtime, four widgets, persisted settings, duplicate launch and main restore'
} finally {
    # Stop only the isolated process this script created, never an existing user instance.
    if (-not $taskProcess.HasExited) {
        $taskNative = Get-CimInstance Win32_Process -Filter ('ProcessId='+$taskProcess.Id)
        if ($taskProcess.StartTime -eq $taskStarted -and $taskNative.ExecutablePath -eq $taskExe -and $taskNative.CommandLine.Contains($taskSettings)) { Stop-Process -Id $taskProcess.Id }
        else { throw 'QA process identity changed; refusing to stop it.' }
    }
}
