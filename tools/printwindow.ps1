param(
  [string]$Exe = "D:\Projects\Dynesshely\Custodian\src\Custodian.App\bin\Debug\net10.0-windows\Custodian.exe",
  [string]$Out = "$env:TEMP\custodian_pw.png",
  [int]$DelaySec = 6,
  [string]$Safe = "",
  [switch]$KeepRunning,
  [switch]$FullContent
)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PW {
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[PW]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing
Get-Process -Name Custodian -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
if ($Safe) { $env:CUSTODIAN_SAFE = $Safe } else { Remove-Item Env:CUSTODIAN_SAFE -ErrorAction SilentlyContinue }
$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $DelaySec
$p.Refresh()
if ($p.HasExited) { "APP EXITED code=" + $p.ExitCode; exit 1 }
$h = $p.MainWindowHandle
$r = New-Object PW+RECT
[PW]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) | Out-Null
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
"window: $w x $ht at $($r.Left),$($r.Top)"
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$flags = 0
if ($FullContent) { $flags = 2 }
$ok = [PW]::PrintWindow($h, $hdc, $flags)
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"printwindow=$ok -> $Out"
if (-not $KeepRunning) { Stop-Process -Id $p.Id -Force; "closed" }