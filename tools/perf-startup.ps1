param(
    [string]$Exe = '',
    [int]$Runs = 3,
    [int]$TimeoutSec = 40,
    [string]$Csv = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'src\Custodian.App\bin\Release\net10.0-windows\Custodian.exe' }
$pipeName = 'Custodian.Debug'

function Invoke-CustodianDebug([string]$command, [int]$timeoutMs = 1500) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($timeoutMs)
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $writer = New-Object System.IO.StreamWriter($pipe, $utf8)
        $reader = New-Object System.IO.StreamReader($pipe, $utf8)
        $writer.AutoFlush = $true
        $writer.WriteLine($command)
        return $reader.ReadLine()
    }
    catch {
        return $null
    }
    finally {
        $pipe.Dispose()
    }
}

$results = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Runs; $i++) {
    Get-Process -Name Custodian -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill() } catch { }
    }
    Start-Sleep -Milliseconds 800

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Exe -ArgumentList '--debug' -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)

    $pingMs = $null
    while ((Get-Date) -lt $deadline) {
        $reply = Invoke-CustodianDebug 'ping' 400
        if ($reply -match '"pong":true') { $pingMs = $sw.Elapsed.TotalMilliseconds; break }
        Start-Sleep -Milliseconds 50
    }

    # 注意：进程刚起来时 busy=false 但卡片还是 0（扫描还没开始），
    # 必须等「扫描完成且确实有卡片」才算可交互
    $readyMs = $null
    $cards = 0
    while ((Get-Date) -lt $deadline) {
        $stateJson = Invoke-CustodianDebug 'state' 800
        if ($stateJson -match '"ok":true') {
            $state = ($stateJson | ConvertFrom-Json).result
            $cards = $state.visibleCards
            if (-not $state.busy -and $cards -gt 0) { $readyMs = $sw.Elapsed.TotalMilliseconds; break }
        }
        Start-Sleep -Milliseconds 50
    }

    $sw.Stop()
    $results.Add([pscustomobject]@{
        run     = $i
        pingMs  = if ($pingMs) { [math]::Round($pingMs, 0) } else { -1 }
        readyMs = if ($readyMs) { [math]::Round($readyMs, 0) } else { -1 }
        cards   = $cards
    })

    try { $process.Kill() } catch { }
    Start-Sleep -Milliseconds 500
}

if ($Csv) { $results | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8; "CSV: $Csv" }

'=== Custodian 冷启动耗时（{0} 次）===' -f $Runs
'dung（调试通道就绪）: min {0} / avg {1:N0} / max {2} ms' -f (($results | Measure-Object pingMs -Minimum).Minimum), (($results | Measure-Object pingMs -Average).Average), (($results | Measure-Object pingMs -Maximum).Maximum)
'可交互（busy=false） : min {0} / avg {1:N0} / max {2} ms' -f (($results | Measure-Object readyMs -Minimum).Minimum), (($results | Measure-Object readyMs -Average).Average), (($results | Measure-Object readyMs -Maximum).Maximum)
$results | Format-Table -AutoSize | Out-String
