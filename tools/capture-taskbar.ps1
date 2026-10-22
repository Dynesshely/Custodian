param(
    [string]$Out = "$env:TEMP\taskbar.png",
    [int]$StripHeight = 220
)

# 抓主显示器底部任务栏条，用于人工/自检图标的实际观感。
$ErrorActionPreference = "Stop"
Add-Type @" 
using System;
using System.Runtime.InteropServices;
public static class TBNative {
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[TBNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing

$taskbar = [TBNative]::FindWindow("Shell_TrayWnd", $null)
if ($taskbar -eq [IntPtr]::Zero) { Write-Error "没找到任务栏"; exit 1 }

$rect = New-Object TBNative+RECT
[TBNative]::GetWindowRect($taskbar, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = [Math]::Min($StripHeight, $rect.Bottom - $rect.Top)

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Bottom - $height, 0, 0, $bitmap.Size)
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

"taskbar: $($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top) -> $Out"
