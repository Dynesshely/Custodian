param(
  [string]$Exe = "D:\Projects\Dynesshely\Custodian\src\Custodian.App\bin\Debug\net10.0-windows\Custodian.exe",
  [string]$Out = "$env:TEMP\custodian_window.png",
  [int]$DelaySec = 6,
  [string]$Safe = "",
  [switch]$KeepRunning,
  [int]$Width = 0,
  [int]$Height = 0
)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SN {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[SN]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Get-Process -Name Custodian -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
if ($Safe) { $env:CUSTODIAN_SAFE = $Safe } else { Remove-Item Env:CUSTODIAN_SAFE -ErrorAction SilentlyContinue }
$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $DelaySec
$p.Refresh()
if ($p.HasExited) { "APP EXITED code=" + $p.ExitCode; exit 1 }
$h = $p.MainWindowHandle
if ($h -eq 0) { "NO WINDOW"; exit 1 }
[SN]::SetWindowPos($h, [IntPtr](-1), 0,0,0,0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
[SN]::SetWindowPos($h, [IntPtr](-2), 0,0,0,0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
[SN]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900
$rect = New-Object SN+RECT
[SN]::DwmGetWindowAttribute($h, 9, [ref]$rect, 16) | Out-Null
Add-Type -AssemblyName System.Drawing
$full = New-Object System.Drawing.Bitmap 5120, 2160
$g = [System.Drawing.Graphics]::FromImage($full)
$g.CopyFromScreen(0, 0, 0, 0, $full.Size)
$w = $rect.Right - $rect.Left; $ht = $rect.Bottom - $rect.Top
"window: $($rect.Left),$($rect.Top) ${w}x${ht}"
$crop = New-Object System.Drawing.Bitmap $w, $ht
$g2 = [System.Drawing.Graphics]::FromImage($crop)
$g2.DrawImage($full, (New-Object System.Drawing.Rectangle 0,0,$w,$ht), (New-Object System.Drawing.Rectangle $rect.Left,$rect.Top,$w,$ht), [System.Drawing.GraphicsUnit]::Pixel)
$crop.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $crop.Dispose(); $g.Dispose(); $full.Dispose()
"SHOT " + $Out
if (-not $KeepRunning) { Stop-Process -Id $p.Id -Force; "closed" }