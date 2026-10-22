param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Command,

    [int]$TimeoutMs = 5000,

    [switch]$Pretty
)

$ErrorActionPreference = "Stop"
$pipeName = "Custodian.Debug"

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect($TimeoutMs)
}
catch {
    Write-Error "无法连接调试管道 '$pipeName'（应用是否以 --debug 或 CUSTODIAN_DEBUG=1 启动？）"
    exit 1
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$writer = New-Object System.IO.StreamWriter($pipe, $utf8)
$reader = New-Object System.IO.StreamReader($pipe, $utf8)
$writer.AutoFlush = $true

$writer.WriteLine($Command)
$response = $reader.ReadLine()

$writer.Dispose()
$reader.Dispose()
$pipe.Dispose()

if (-not $response) {
    Write-Error "调试通道没有返回内容"
    exit 1
}

if ($Pretty) {
    $response | ConvertFrom-Json | ConvertTo-Json -Depth 8
}
else {
    $response
}
