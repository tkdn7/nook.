$ErrorActionPreference = 'Stop'
# Run with powershell -STA. Original Fluent Apps geometry, theme colors only.
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskAsset = Join-Path $taskRoot 'src/Nook.App/Assets'
[xml]$taskSvg = Get-Content -LiteralPath (Join-Path $taskAsset 'Icons/apps.svg') -Raw
$taskData = 'F1 ' + (($taskSvg.SelectNodes('//*[local-name()="path"]') | ForEach-Object { $_.d }) -join ' ')
$taskGeometry = [System.Windows.Media.Geometry]::Parse($taskData)
$taskVisual = [System.Windows.Media.DrawingVisual]::new()
$taskContext = $taskVisual.RenderOpen()
$taskInk = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#242424'))
$taskCream = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#F6F0E6'))
$taskContext.DrawRoundedRectangle($taskInk, $null, [System.Windows.Rect]::new(4, 4, 248, 248), 52, 52)
$taskContext.PushTransform([System.Windows.Media.TranslateTransform]::new(32, 32))
$taskContext.PushTransform([System.Windows.Media.ScaleTransform]::new(8, 8))
$taskContext.DrawGeometry($taskCream, $null, $taskGeometry)
$taskContext.Pop(); $taskContext.Pop(); $taskContext.Close()
$taskBitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(256, 256, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$taskBitmap.Render($taskVisual)
$taskEncoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$taskEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
$taskPng = [System.IO.MemoryStream]::new()
$taskEncoder.Save($taskPng)
$taskBytes = $taskPng.ToArray()
$taskStream = [System.IO.File]::Create((Join-Path $taskAsset 'nook.ico'))
$taskWriter = [System.IO.BinaryWriter]::new($taskStream)
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]1)
    $taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
    $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskBytes.Length); $taskWriter.Write([uint32]22)
    $taskWriter.Write($taskBytes)
} finally { $taskWriter.Dispose(); $taskPng.Dispose() }
