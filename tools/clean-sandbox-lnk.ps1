param(
    # 沙盒根目录（默认读 Sandboxie 的 FileRootPath 形态：<根>\<用户>\<沙盒>）
    [string]$Root = 'D:\Sandbox\' + $env:USERNAME,
    # 只统计不落盘
    [switch]$DryRun,
    # 连「没有宿主孪生文件」的宿主应用快捷方式也一起清（默认保留，可能是你在盒内自己建的）
    [switch]$IncludeUnowned,
    [string]$QuarantineRoot = (Join-Path $env:LOCALAPPDATA 'Custodian\backup')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Root)) {
    throw "沙盒根目录不存在：$Root"
}

$shell = New-Object -ComObject WScript.Shell
$userProfile = $env:USERPROFILE
$programData = $env:ProgramData
$publicRoot = 'C:\Users\Public'

function Get-HostTwin([string]$relativePath) {
    $r = $relativePath.TrimStart('\')
    if ($r.StartsWith('user\current\')) { return Join-Path $userProfile $r.Substring(13) }
    if ($r.StartsWith('user\all\')) { return Join-Path $programData $r.Substring(9) }
    if ($r.StartsWith('user\public\')) { return Join-Path $publicRoot $r.Substring(12) }
    if ($r.StartsWith('drive\')) {
        $parts = $r.Split('\', 3)
        if ($parts.Count -eq 3) { return $parts[1] + ':\' + $parts[2] }
    }
    return $null
}

function Test-InBox([string]$boxRoot, [string]$target) {
    if ([string]::IsNullOrWhiteSpace($target) -or $target.Length -lt 4) { return $false }
    if ($target.StartsWith($boxRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        return (Test-Path -LiteralPath $target -PathType Leaf)
    }
    if ($target -match '^[A-Za-z]:\\') {
        $candidate = Join-Path $boxRoot ('drive\' + $target.Substring(0, 1) + '\' + $target.Substring(3))
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $true }
    }
    foreach ($pair in @(@($userProfile, 'user\current'), @($programData, 'user\all'), @($publicRoot, 'user\public'))) {
        $hostRoot = $pair[0]
        if ($hostRoot.Length -eq 0 -or -not $target.StartsWith($hostRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($target.Length -gt $hostRoot.Length -and $target[$hostRoot.Length] -ne '\') { continue }
        $candidate = Join-Path $boxRoot ($pair[1] + '\' + $target.Substring($hostRoot.Length).TrimStart('\'))
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $true }
    }
    return $false
}

$items = New-Object System.Collections.Generic.List[object]
foreach ($box in (Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue)) {
    foreach ($file in (Get-ChildItem -LiteralPath $box.FullName -Recurse -Filter *.lnk -File -ErrorAction SilentlyContinue)) {
        if ($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { continue }

        $relative = $file.FullName.Substring($box.FullName.Length + 1)
        $target = ''
        try { $target = $shell.CreateShortcut($file.FullName).TargetPath } catch { $target = '' }

        $hostTwin = Get-HostTwin $relative
        $hasTwin = $hostTwin -and (Test-Path -LiteralPath $hostTwin -PathType Leaf)
        $targetIsHost = $target -and (Test-Path -LiteralPath $target -PathType Leaf)

        $category =
            if (Test-InBox $box.FullName $target) { 'box-app' }
            elseif ($hasTwin) { 'host-copy' }
            elseif ($targetIsHost) { 'sandbox-made' }
            else { 'dead' }

        # 指向别的沙盒里程序的快捷方式（跨盒钉住项）不算宿主机应用，永远保留
        $crossBox = $target -and $target.StartsWith($Root.TrimEnd('') + '', [StringComparison]::OrdinalIgnoreCase)

        $remove =
            $category -eq 'host-copy' -or
            $category -eq 'dead' -or
            ($IncludeUnowned -and $category -eq 'sandbox-made' -and $targetIsHost -and -not $crossBox)

        $items.Add([pscustomobject]@{
            Box       = $box.Name
            BoxRoot   = $box.FullName
            FullPath  = $file.FullName
            Relative  = $relative
            Target    = $target
            Category  = $category
            Remove    = $remove
            Bytes     = $file.Length
            HostTwin  = $hostTwin
        })
    }
}

Write-Output ('扫描：' + $Root + '  共 ' + $items.Count + ' 个 .lnk')
foreach ($group in ($items | Group-Object Category | Sort-Object Name)) {
    Write-Output ('  {0,4}  {1}' -f $group.Count, $group.Name)
}

$toRemove = @($items | Where-Object Remove)
$kept = @($items | Where-Object { -not $_.Remove })
Write-Output ''
Write-Output ('待清理 ' + $toRemove.Count + ' 个（' + [math]::Round((($toRemove | Measure-Object Bytes -Sum).Sum / 1KB), 1) + ' KB），保留 ' + $kept.Count + ' 个')

if ($DryRun) {
    Write-Output ''
    Write-Output '--- 待清理清单（DryRun） ---'
    $toRemove | ForEach-Object { '  {0,-32} {1,-12} {2}' -f $_.Relative, $_.Category, $_.Target }
    Write-Output ''
    Write-Output '--- 保留清单 ---'
    $kept | ForEach-Object { '  {0,-32} {1,-12} {2}' -f $_.Relative, $_.Category, $_.Target }
    return
}

if ($toRemove.Count -eq 0) {
    Write-Output '没有需要清理的文件。'
    return
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$destination = Join-Path $QuarantineRoot ('sandbox-lnk-' + $stamp)
New-Item -ItemType Directory -Path $destination -Force | Out-Null

# 安全护栏：只允许动沙盒根目录下的文件
$rootFull = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\') + '\'

$manifest = New-Object System.Collections.Generic.List[object]
$failed = 0
foreach ($item in $toRemove) {
    $full = [IO.Path]::GetFullPath($item.FullPath)
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning ('跳过（不在沙盒根下）：' + $full)
        $failed++
        continue
    }

    $relativeToRoot = $full.Substring($rootFull.Length)
    $targetFile = Join-Path $destination $relativeToRoot
    $targetDir = Split-Path $targetFile -Parent
    try {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        Move-Item -LiteralPath $full -Destination $targetFile
        $manifest.Add([pscustomobject]@{
            box      = $item.Box
            original = $full
            backup   = $targetFile
            category = $item.Category
            target   = $item.Target
        })
    }
    catch {
        Write-Warning ('移动失败：' + $full + ' —— ' + $_.Exception.Message)
        $failed++
    }
}

$manifestPath = Join-Path $destination 'manifest.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$restore = @'
param([switch]$DryRun)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Get-Content -LiteralPath (Join-Path $here 'manifest.json') -Raw | ConvertFrom-Json
$count = 0
foreach ($entry in $manifest) {
    if (-not (Test-Path -LiteralPath $entry.backup)) { continue }
    if (Test-Path -LiteralPath $entry.original) { continue }
    if ($DryRun) { Write-Output ('would restore ' + $entry.original); $count++; continue }
    $dir = Split-Path $entry.original -Parent
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Move-Item -LiteralPath $entry.backup -Destination $entry.original
    $count++
}
Write-Output ('restored ' + $count + ' file(s)')
'@
Set-Content -LiteralPath (Join-Path $destination 'restore.ps1') -Value $restore -Encoding UTF8

# 收掉清理后变空的分支目录（自底向上，只动沙盒内部）
$removedDirs = 0
foreach ($box in (Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue)) {
    $directories = Get-ChildItem -LiteralPath $box.FullName -Directory -Recurse -ErrorAction SilentlyContinue |
        Sort-Object { $_.FullName.Length } -Descending
    foreach ($directory in $directories) {
        if (-not (Test-Path -LiteralPath $directory.FullName)) { continue }
        if ($directory.FullName.TrimEnd('\') -eq $box.FullName.TrimEnd('\')) { continue }

        # 只删「确认为空」的叶子目录：枚举失败（权限等）一律当作非空，且不带 -Recurse，
        # 这样即使判断出错也绝不会连带删掉里面的文件。
        try {
            $children = @(Get-ChildItem -LiteralPath $directory.FullName -Force -ErrorAction Stop)
        }
        catch {
            continue
        }

        if ($children.Count -eq 0) {
            Remove-Item -LiteralPath $directory.FullName -Force -ErrorAction SilentlyContinue
            $removedDirs++
        }
    }
}

Write-Output ''
Write-Output ('已移动 ' + $manifest.Count + ' 个文件到：' + $destination + '（失败 ' + $failed + '）')
Write-Output ('清单：' + $manifestPath)
Write-Output ('回滚：pwsh -NoProfile -File "' + (Join-Path $destination 'restore.ps1') + '"')
Write-Output ('顺手清掉空目录 ' + $removedDirs + ' 个')
