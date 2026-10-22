param(
    [int]$Rounds = 20,
    [int]$MemoryOpens = 40,
    [string]$Label = 'variant'
)

$ErrorActionPreference = 'Stop'

function Invoke-Debug([string]$command) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'Custodian.Debug', [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(1500)
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $writer = New-Object System.IO.StreamWriter($pipe, $utf8); $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($pipe, $utf8)
        $writer.WriteLine($command)
        return $reader.ReadLine()
    }
    catch { return $null }
    finally { $pipe.Dispose() }
}

function Invoke-Json([string]$command) {
    $json = Invoke-Debug $command
    if ($json -notmatch '"ok":true') { return $null }
    return ($json | ConvertFrom-Json).result
}

$process = Get-Process Custodian | Sort-Object StartTime -Descending | Select-Object -First 1
if (-not $process) { throw 'Custodian 没在跑' }

$null = Invoke-Debug 'show'
Start-Sleep -Seconds 2

$idle = Invoke-Json 'mem'

# 键盘路径：Enter 开 / Esc 关（真实交互路径）
$null = Invoke-Debug 'press End'
$process.Refresh()
$cpu0 = $process.TotalProcessorTime.TotalMilliseconds
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$opens = @()
for ($i = 0; $i -lt $Rounds; $i++) {
    $openWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Invoke-Debug 'press Enter'
    $openWatch.Stop()
    $opens += $openWatch.Elapsed.TotalMilliseconds
    $null = Invoke-Debug 'press Escape'
}
$stopwatch.Stop()
$process.Refresh()
$cpu1 = $process.TotalProcessorTime.TotalMilliseconds
$cpuPerPair = ($cpu1 - $cpu0) / $Rounds

# 内存：连开 40 次后静置
for ($i = 0; $i -lt $MemoryOpens; $i++) {
    $null = Invoke-Debug 'press Enter'
    $null = Invoke-Debug 'press Escape'
}
Start-Sleep -Seconds 4
$loaded = Invoke-Json 'mem'

[pscustomobject]@{
    label          = $Label
    idleWs         = $idle.workingSetMb
    idlePrivate    = $idle.privateMb
    loadedWs       = $loaded.workingSetMb
    loadedPrivate  = $loaded.privateMb
    loadedManaged  = $loaded.managedMb
    openColdMs     = [math]::Round($opens[0], 1)
    openAvgMs      = [math]::Round((($opens | Measure-Object -Average).Average), 1)
    openSteadyMs   = [math]::Round((($opens | Select-Object -Skip 1 | Measure-Object -Average).Average), 1)
    openMaxMs      = [math]::Round(($opens | Measure-Object -Maximum).Maximum, 1)
    cpuPerPairMs   = [math]::Round($cpuPerPair, 1)
    gdi            = $loaded.gdi
    handles        = $loaded.handles
} | Format-List | Out-String
