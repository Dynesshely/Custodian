param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Label = 'variant',
    [int]$Runs = 3,
    [int]$StressOpens = 40,
    [string]$Csv = ''
)

$ErrorActionPreference = 'Stop'
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
    catch { return $null }
    finally { $pipe.Dispose() }
}

function Get-Mem {
    $json = Invoke-CustodianDebug 'mem'
    if ($json -notmatch '"ok":true') { return $null }
    return ($json | ConvertFrom-Json).result
}

$rows = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Runs; $i++) {
    Get-Process -Name Custodian -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    Start-Sleep -Milliseconds 800

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Exe -ArgumentList '--debug' -PassThru
    $deadline = (Get-Date).AddSeconds(40)

    $pingMs = $null
    while ((Get-Date) -lt $deadline) {
        $reply = Invoke-CustodianDebug 'ping' 400
        if ($reply -match '"pong":true') { $pingMs = $sw.Elapsed.TotalMilliseconds; break }
        Start-Sleep -Milliseconds 50
    }

    $readyMs = $null
    while ((Get-Date) -lt $deadline) {
        $stateJson = Invoke-CustodianDebug 'state' 800
        if ($stateJson -match '"ok":true') {
            $state = ($stateJson | ConvertFrom-Json).result
            if (-not $state.busy -and $state.visibleCards -gt 0) { $readyMs = $sw.Elapsed.TotalMilliseconds; break }
        }
        Start-Sleep -Milliseconds 50
    }

    # 稳定后再取「空闲内存」
    Start-Sleep -Seconds 3
    $process.Refresh()
    $idle = Get-Mem

    # 压力：反复开关文件夹弹窗（会用到 BlurEffect 的渲染目标，最容易把内存顶起来）
    for ($round = 0; $round -lt $StressOpens; $round++) {
        $null = Invoke-CustodianDebug "folder $sandbox"
        $null = Invoke-CustodianDebug 'press Escape'
    }
    $null = Invoke-CustodianDebug 'filter '
    Start-Sleep -Seconds 4
    $process.Refresh()
    $stressed = Get-Mem

    $rows.Add([pscustomobject]@{
        variant      = $Label
        run          = $i
        pingMs       = if ($pingMs) { [math]::Round($pingMs, 0) } else { -1 }
        readyMs      = if ($readyMs) { [math]::Round($readyMs, 0) } else { -1 }
        idleWs       = $idle.workingSetMb
        idlePrivate  = $idle.privateMb
        idleManaged  = $idle.managedMb
        idleHeap     = $idle.heapSizeMb
        afterWs      = $stressed.workingSetMb
        afterPrivate = $stressed.privateMb
        afterManaged = $stressed.managedMb
        afterHeap    = $stressed.heapSizeMb
        gen2         = $stressed.gen2
        handles      = $stressed.handles
        gdi          = $stressed.gdi
    })

    try { $process.Kill() } catch { }
    Start-Sleep -Milliseconds 600
}

if ($Csv) {
    $dir = Split-Path -Parent $Csv
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $rows | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8
    "CSV: $Csv"
}

$rows | Format-Table -AutoSize | Out-String