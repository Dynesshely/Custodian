param(
    [switch]$NoBuild,
    [switch]$NoDebug,
    [int]$WaitSeconds = 20
)

# 保持一个开发实例常驻：构建（增量）→ 结束旧实例 → 启动新实例 → 等调试通道就绪。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\Custodian.App\bin\Release\net10.0-windows\Custodian.exe"
$debugClient = Join-Path $root "tools\custodian-debug.ps1"

Get-Process -Name Custodian -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}

if (-not $NoBuild) {
    Push-Location $root
    try {
        dotnet build "src\Custodian.App\Custodian.App.csproj" -c Release -v q --nologo | Select-Object -Last 3
    }
    finally {
        Pop-Location
    }
}

$arguments = @()
if (-not $NoDebug) { $arguments += "--debug" }

$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru

$deadline = (Get-Date).AddSeconds($WaitSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if (-not $NoDebug) {
        try {
            $response = & pwsh -NoProfile -File $debugClient "ping" 2>$null
            if ($response -match '"pong":true') {
                $ready = $true
                break
            }
        }
        catch {
            # 通道还没起来，继续等
        }
    }
}

if ($NoDebug) {
    Start-Sleep -Seconds 2
    $ready = $true
}

$process.Refresh()
if ($process.HasExited) {
    Write-Error "实例启动失败，退出码 $($process.ExitCode)"
    exit 1
}

"dev instance: pid=" + $process.Id + " ready=" + $ready
if (-not $NoDebug) { & pwsh -NoProfile -File $debugClient "state" }
