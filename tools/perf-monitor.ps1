param(
    [string]$ProcessName = 'Custodian',
    [int]$Seconds = 30,
    [int]$SampleMs = 1000,
    [string]$Csv = '',
    # 采样期间同时跑一圈交互场景（切左栏 / 切视图 / 开文件夹 / 搜索）
    [switch]$Drive,
    # 采样前把面板收进托盘，测「后台常驻开销」
    [switch]$Hidden,
    # 只跑压力测试：N 轮「切左栏 / 筛选 / 切视图」，统计耗时与句柄、内存变化
    [int]$Stress = 0
)

$ErrorActionPreference = 'Stop'
$pipeName = 'Custodian.Debug'
$cpuCount = [Environment]::ProcessorCount

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PerfNative {
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetGuiResources(IntPtr process, uint flags);
}
"@

function Invoke-CustodianDebug([string]$command) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(2000)
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

function Get-CustodianProcess {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
}

function Get-Stat($values) {
    $sorted = @($values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 0) { return $null }
    [pscustomobject]@{
        min = $sorted[0]
        avg = [math]::Round((($sorted | Measure-Object -Average).Average), 2)
        p95 = $sorted[[math]::Min($n - 1, [int][math]::Floor($n * 0.95))]
        max = $sorted[$n - 1]
    }
}

function Format-Stat($values) {
    $s = Get-Stat $values
    if ($null -eq $s) { return '-' }
    'min {0} / avg {1} / p95 {2} / max {3}' -f $s.min, $s.avg, $s.p95, $s.max
}

function Get-FirstSandboxName {
    $json = Invoke-CustodianDebug 'state'
    if ($json -match '"ok":true') {
        $sections = ($json | ConvertFrom-Json).result.sections
        $candidate = $sections | Where-Object { $_.items -gt 1 } | Select-Object -First 1
        if ($candidate) { return $candidate.name }
    }
    return 'DefaultBox'
}

$sandbox = Get-FirstSandboxName

$target = Get-CustodianProcess
if (-not $target) { throw "没找到进程 $ProcessName（先跑 tools/run-dev.ps1）" }

if ($Stress -gt 0) {
    $null = Invoke-CustodianDebug 'show'
    Start-Sleep -Milliseconds 500

    $before = [pscustomobject]@{
        ws = [math]::Round($target.WorkingSet64 / 1MB, 1)
        private = [math]::Round($target.PrivateMemorySize64 / 1MB, 1)
        handles = $target.HandleCount
        gdi = [PerfNative]::GetGuiResources($target.Handle, 0)
        user = [PerfNative]::GetGuiResources($target.Handle, 1)
    }

    $cycle = @(
        @{ name = 'rail:全部'; cmd = 'rail all' },
        @{ name = 'rail:section'; cmd = 'rail 1' },
        @{ name = 'filter:微信'; cmd = 'filter 微信' },
        @{ name = 'filter:清空'; cmd = 'filter ' },
        @{ name = 'view:compact'; cmd = 'view compact' },
        @{ name = 'view:standard'; cmd = 'view standard' },
        @{ name = 'folder:open'; cmd = "folder $sandbox" },
        @{ name = 'folder:close'; cmd = 'press Escape' }
    )

    $timings = New-Object System.Collections.Generic.List[object]
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    for ($round = 0; $round -lt $Stress; $round++) {
        foreach ($action in $cycle) {
            $op = [System.Diagnostics.Stopwatch]::StartNew()
            $null = Invoke-CustodianDebug $action.cmd
            $op.Stop()
            $timings.Add([pscustomobject]@{ action = $action.name; ms = $op.Elapsed.TotalMilliseconds })
        }
    }
    $sw.Stop()

    $null = Invoke-CustodianDebug 'filter '
    $null = Invoke-CustodianDebug 'rail all'
    Start-Sleep -Milliseconds 800
    $target.Refresh()

    $after = [pscustomobject]@{
        ws = [math]::Round($target.WorkingSet64 / 1MB, 1)
        private = [math]::Round($target.PrivateMemorySize64 / 1MB, 1)
        handles = $target.HandleCount
        gdi = [PerfNative]::GetGuiResources($target.Handle, 0)
        user = [PerfNative]::GetGuiResources($target.Handle, 1)
    }

    ''
    '=== 压力测试：{0} 轮 × {1} 个操作 = {2} 次命令，总耗时 {3:N1} 秒 ===' -f $Stress, $cycle.Count, ($Stress * $cycle.Count), $sw.Elapsed.TotalSeconds
    $timings | Group-Object action | ForEach-Object {
        $avg = ($_.Group | Measure-Object -Property ms -Average).Average
        $p95 = ($_.Group | Sort-Object ms | Select-Object -First ([math]::Max(1, [int]($_.Count * 0.95)))) | Select-Object -Last 1
        '  {0,-22} avg {1,7:N2} ms   p95 {2,7:N2} ms   max {3,7:N2} ms' -f $_.Name, $avg, $p95.ms, (($_.Group | Measure-Object -Property ms -Maximum).Maximum)
    }
    ''
    '  内存/句柄：WS {0}→{1} MB, Private {2}→{3} MB, Handles {4}→{5}, GDI {6}→{7}, USER {8}→{9}' -f $before.ws, $after.ws, $before.private, $after.private, $before.handles, $after.handles, $before.gdi, $after.gdi, $before.user, $after.user
    return
}

if ($Hidden) { $null = Invoke-CustodianDebug 'hide' } else { $null = Invoke-CustodianDebug 'show' }
Start-Sleep -Milliseconds 400

$rows = New-Object System.Collections.Generic.List[object]
$lastCpu = $target.TotalProcessorTime.TotalMilliseconds
$lastCpuAt = Get-Date
$started = Get-Date
$scenarios = New-Object System.Collections.Generic.List[object]

$actions = @(
    @{ name = 'rail:全部'; cmd = 'rail all' },
    @{ name = 'rail:section'; cmd = 'rail 1' },
    @{ name = 'filter:微信'; cmd = 'filter 微信' },
    @{ name = 'filter:清空'; cmd = 'filter ' },
    @{ name = 'view:compact'; cmd = 'view compact' },
    @{ name = 'view:standard'; cmd = 'view standard' },
    @{ name = 'folder:open'; cmd = "folder $sandbox" },
    @{ name = 'folder:close'; cmd = 'press Escape' }
)

$deadline = (Get-Date).AddSeconds($Seconds)
$index = 0
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds $SampleMs

    $target.Refresh()
    if ($target.HasExited) { throw '进程已退出' }
    $now = Get-Date
    $cpuNow = $target.TotalProcessorTime.TotalMilliseconds
    $elapsed = ($now - $lastCpuAt).TotalMilliseconds
    $cpuPercent = 0.0
    if ($elapsed -gt 0) { $cpuPercent = (($cpuNow - $lastCpu) / $elapsed) * 100.0 }
    $lastCpu = $cpuNow
    $lastCpuAt = $now

    $row = [pscustomobject]@{
        t          = [math]::Round(($now - $started).TotalSeconds, 1)
        cpuCorePct = [math]::Round($cpuPercent, 2)
        cpuPct     = [math]::Round($cpuPercent / $cpuCount, 3)
        wsMb       = [math]::Round($target.WorkingSet64 / 1MB, 1)
        privateMb  = [math]::Round($target.PrivateMemorySize64 / 1MB, 1)
        threads    = $target.Threads.Count
        handles    = $target.HandleCount
        gdi        = [PerfNative]::GetGuiResources($target.Handle, 0)
        user       = [PerfNative]::GetGuiResources($target.Handle, 1)
        action     = ''
        managedMb  = $null
        heapMb     = $null
        gen2       = $null
    }

    if ($rows.Count -eq 0 -or ($rows.Count % 5) -eq 0) {
        $memJson = Invoke-CustodianDebug 'mem'
        if ($memJson -match '"ok":true') {
            $mem = ($memJson | ConvertFrom-Json).result
            $row.managedMb = $mem.managedMb
            $row.heapMb = $mem.heapSizeMb
            $row.gen2 = $mem.gen2
        }
    }

    if ($Drive) {
        $action = $actions[$index % $actions.Count]
        $index++
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-CustodianDebug $action.cmd
        $sw.Stop()
        $row.action = $action.name
        $scenarios.Add([pscustomobject]@{ action = $action.name; ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 2) })
    }

    $rows.Add($row)
    $progress = [math]::Min(100, (($now - $started).TotalSeconds / $Seconds) * 100)
    Write-Progress -Activity 'Custodian 性能采样' -Status ("CPU {0:N2}% 单核 / WS {1:N1} MB" -f $row.cpuCorePct, $row.wsMb) -PercentComplete $progress
}

Write-Progress -Activity 'Custodian 性能采样' -Completed

if ($Csv) {
    $rows | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8
    "CSV: $Csv"
}

$firstRow = $rows[0]
$lastRow = $rows[$rows.Count - 1]

''
'=== Custodian 性能采样：{0} 个样本 / {1}s / {2} 逻辑核 / {3} ===' -f $rows.Count, $Seconds, $cpuCount, ($(if ($Hidden) { '窗口在托盘（后台常驻）' } else { '窗口可见（空闲）' }))
'CPU 单核%   : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.cpuCorePct }))
'CPU 整机%   : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.cpuPct }))
'WS MB       : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.wsMb }))
'Private MB  : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.privateMb }))
'托管堆 MB   : {0}' -f (Format-Stat ($rows | Where-Object { $null -ne $_.managedMb } | ForEach-Object { $_.managedMb }))
'GC 堆 MB    : {0}' -f (Format-Stat ($rows | Where-Object { $null -ne $_.heapMb } | ForEach-Object { $_.heapMb }))
'Handles     : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.handles }))
'GDI 对象    : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.gdi }))
'USER 对象   : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.user }))
'线程        : {0}' -f (Format-Stat ($rows | ForEach-Object { $_.threads }))
''
'CPU 累计    : {0:N2} 秒（单核当量）' -f (($rows | Measure-Object -Property cpuCorePct -Sum).Sum / 100.0 * ($SampleMs / 1000.0))
'首末对比    : WS {0:N1}→{1:N1} MB, Private {2:N1}→{3:N1} MB, Handles {4}→{5}, GDI {6}→{7}, USER {8}→{9}' -f $firstRow.wsMb, $lastRow.wsMb, $firstRow.privateMb, $lastRow.privateMb, $firstRow.handles, $lastRow.handles, $firstRow.gdi, $lastRow.gdi, $firstRow.user, $lastRow.user

if ($scenarios.Count -gt 0) {
    ''
    '=== 交互往返耗时（应用内调试命令，含命令处理 + 布局）==='
    $scenarios | Group-Object action | ForEach-Object {
        $avg = ($_.Group | Measure-Object -Property ms -Average).Average
        $max = ($_.Group | Measure-Object -Property ms -Maximum).Maximum
        '  {0,-22} avg {1,6:N2} ms   max {2,6:N2} ms' -f $_.Name, $avg, $max
    }
}