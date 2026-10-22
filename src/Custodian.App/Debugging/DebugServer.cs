using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Custodian.App.Debugging;

/// <summary>
/// 进程内调试通道：命名管道 + 逐行 JSON 命令，供自动化验证使用。
/// 目的：不注入键鼠、不抢用户鼠标，也能确定性地驱动面板（点击/筛选/切换/截图）。
/// 仅在 <c>--debug</c> 参数或 <c>CUSTODIAN_DEBUG=1</c> 时启动。
/// </summary>
public sealed class DebugServer : IDisposable
{
    public const string PipeName = "Custodian.Debug";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly Func<string, DebugResponse> _handler;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _server;

    public DebugServer(Func<string, DebugResponse> handler) => _handler = handler;

    public static bool IsEnabled =>
        Environment.GetCommandLineArgs().Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase)) ||
        Environment.GetEnvironmentVariable("CUSTODIAN_DEBUG") == "1";

    public static string DiscoveryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Custodian",
        "debug.json");

    public void Start()
    {
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // 单连接串行处理：命令都很短，串行更可控，也避免同一管道出现多个监听实例
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    8192,
                    8192);
                _server = server;
                await server.WaitForConnectionAsync(_cts.Token);
                await ServeAsync(server);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Services.Diagnostics.Write("debug: loop error " + ex.Message);
                try
                {
                    await Task.Delay(300, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server)
    {
        try
        {
            using (server)
            using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 8192, leaveOpen: true))
            using (var writer = new StreamWriter(server, new UTF8Encoding(false), 8192, leaveOpen: true))
            {
                writer.AutoFlush = true;

                while (!_cts.IsCancellationRequested && server.IsConnected)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line is null)
                    {
                        return;
                    }

                    line = line.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    DebugResponse response;
                    try
                    {
                        response = _handler(line);
                    }
                    catch (Exception ex)
                    {
                        response = DebugResponse.Fail(ex.GetType().Name + ": " + ex.Message);
                    }

                    var json = response.Ok
                        ? "{\"ok\":true,\"result\":" + (response.Result ?? "null") + "}"
                        : "{\"ok\":false,\"error\":" + JsonSerializer.Serialize(response.Error ?? "unknown", JsonOptions) + "}";

                    await writer.WriteLineAsync(json);
                    Services.Diagnostics.Write("debug: " + line + " -> " + (response.Ok ? "ok" : "fail"));

                    if (response.Close)
                    {
                        return;
                    }
                }
            }
        }
        catch (IOException)
        {
            // 客户端读完一行就断开，属正常行为
        }
        catch (Exception ex)
        {
            Services.Diagnostics.Write("debug: session error " + ex.Message);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _server?.Dispose();
        }
        catch
        {
            // ignore
        }

        _cts.Dispose();
    }
}

/// <summary>调试命令的统一返回。Result 为已序列化的 JSON 文本。</summary>
public sealed record DebugResponse(bool Ok, string? Result, string? Error = null, bool Close = false)
{
    public static DebugResponse Success(string? json = null) => new(true, json ?? "null");

    public static DebugResponse Fail(string error) => new(false, null, error);

    public static DebugResponse Shutdown(string? json = null) => new(true, json ?? "null", null, true);
}
